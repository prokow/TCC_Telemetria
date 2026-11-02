-- Depois de 002/003/005. Executar INTEIRO no SQL Editor do mesmo projeto.
-- Banco e Storage privados; nenhuma chave administrativa no Unity.
begin;
do $$ begin
  if to_regprocedure('public.participant_telemetry_access(text)') is null then
    raise exception 'Execute primeiro 005_participant_telemetry_analysis.sql.';
  end if;
end $$;

create schema if not exists telemetry_private;
revoke all on schema telemetry_private from public, anon, authenticated;
create table if not exists telemetry_private.operators (
  user_id uuid primary key references auth.users(id) on delete cascade,
  active boolean not null default true
);
revoke all on telemetry_private.operators from public, anon, authenticated;
-- Aproveita o UUID ja autorizado pela migracao 003, sem solicitar outro UUID.
insert into telemetry_private.operators(user_id)
select distinct u.id from auth.users u join (
  select (regexp_matches(qual, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', 'g'))[1]::uuid as user_id
  from pg_policies where schemaname='public' and tablename='participants'
    and policyname='participants_read_menu_operator' and cmd='SELECT'
) p on p.user_id=u.id where true on conflict do nothing;
do $$ begin
  if not exists(select 1 from telemetry_private.operators where active) then
    raise exception 'Nenhum responsavel autorizado. Execute 003 com o UUID correto antes desta migracao.';
  end if;
end $$;

create or replace function public.is_telemetry_operator()
returns boolean language sql stable security definer set search_path=''
as $$ select auth.uid() is not null and exists(
  select 1 from telemetry_private.operators where user_id=auth.uid() and active
); $$;
revoke all on function public.is_telemetry_operator() from public, anon;
grant execute on function public.is_telemetry_operator() to authenticated;

-- Uma unica autorizacao por UUID de Authentication controla cadastro, leitura e ingestao.
drop policy if exists participants_read_menu_operator on public.participants;
create policy participants_read_menu_operator on public.participants for select to authenticated
using ((select public.is_telemetry_operator()));
drop policy if exists participants_insert_web_operator on public.participants;
create policy participants_insert_web_operator on public.participants for insert to authenticated
with check ((select public.is_telemetry_operator()) and participant_code ~ '^[0-9a-f]{32}$'
  and display_name is not null and display_name=btrim(display_name)
  and char_length(display_name) between 1 and 120 and display_name !~ '[[:cntrl:]]');

alter table public.sessions add column if not exists uploaded_by uuid references auth.users(id);
alter table public.sessions add column if not exists upload_digest text;
alter table public.sessions add column if not exists uploaded_at timestamptz;
alter table public.sessions add column if not exists sample_counts jsonb;
alter table public.session_events add column if not exists event_index integer;
create unique index if not exists idx_session_events_session_index
on public.session_events(session_id,event_index) where event_index is not null;
alter table public.session_files add column if not exists sha256 text;
alter table public.session_files add column if not exists content_type text;
-- Escrita somente pela RPC validada; leitura continua pelas politicas da migracao 005.
revoke insert, update, delete on public.sessions, public.session_events, public.session_files, public.tasks from anon, authenticated;

insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('vr-telemetry','vr-telemetry',false,67108864,array['text/csv','application/json'])
on conflict(id) do update set public=false,file_size_limit=excluded.file_size_limit,allowed_mime_types=excluded.allowed_mime_types;

drop policy if exists telemetry_operator_upload on storage.objects;
create policy telemetry_operator_upload on storage.objects for insert to authenticated
with check (bucket_id='vr-telemetry' and (select public.is_telemetry_operator())
  and split_part(name,'/',1)=(select auth.uid())::text
  and split_part(name,'/',2) ~ '^[0-9a-f]{32}$' and split_part(name,'/',3) ~ '^[0-9a-f]{32}$'
  and split_part(name,'/',4) in ('Session.json','Head.csv','Controllers.csv','Events.csv')
  and array_length(string_to_array(name,'/'),1)=4
  and exists(select 1 from public.participants p where p.participant_code=split_part(name,'/',2)));
drop policy if exists telemetry_operator_download on storage.objects;
create policy telemetry_operator_download on storage.objects for select to authenticated
using (bucket_id='vr-telemetry' and (select public.is_telemetry_operator()) and (
  (split_part(name,'/',1)=(select auth.uid())::text
    and exists(select 1 from public.participants p where p.participant_code=split_part(name,'/',2)))
  or exists(select 1 from public.session_files f where f.bucket_id='vr-telemetry' and f.storage_path=name)
));

create or replace function public.ingest_telemetry_session(p_session jsonb, p_events jsonb, p_files jsonb, p_digest text)
returns jsonb language plpgsql security definer set search_path=''
as $$
declare
  operator_id uuid := auth.uid();
  session_code text := p_session->>'session_id';
  participant text := p_session->>'participant_code';
  existing public.sessions%rowtype;
  item jsonb;
  file_name text;
  path_prefix text;
  expected_type text;
  expected_mime text;
  started timestamptz;
  ended timestamptz;
  event_item jsonb;
  task_id text;
  sample_field text;
begin
  if operator_id is null or not public.is_telemetry_operator() then
    raise exception using errcode='42501',message='Responsavel nao autorizado para telemetria.';
  end if;
  if jsonb_typeof(p_session) is distinct from 'object' or jsonb_typeof(p_events) is distinct from 'array'
     or jsonb_typeof(p_files) is distinct from 'array' then
    raise exception using errcode='22023',message='Formato da ingestao invalido.';
  end if;
  if (session_code ~ '^[0-9a-f]{32}$' and participant ~ '^[0-9a-f]{32}$' and p_digest ~ '^[0-9a-f]{64}$') is not true then
    raise exception using errcode='22023',message='UUID ou digest invalido.';
  end if;
  if not exists(select 1 from public.participants where participant_code=participant) then
    raise exception using errcode='23503',message='UUID do participante nao cadastrado.';
  end if;
  if jsonb_array_length(p_events)>10000 or jsonb_array_length(p_files)<>4 then
    raise exception using errcode='22023',message='Quantidade de eventos ou arquivos invalida.';
  end if;
  if ((p_session->>'attempts')::integer>=0 and (p_session->>'correct_answers')::integer>=0
     and (p_session->>'errors')::integer>=0
     and (p_session->>'attempts')::integer=(p_session->>'correct_answers')::integer+(p_session->>'errors')::integer
     and (p_session->>'duration_seconds')::numeric>=0 and (p_session->>'duration_seconds')::numeric<=604800
     and (p_session->>'schema_version')::integer between 1 and 100
     and length(p_session->>'application_version')<=200 and length(p_session->>'coordinate_space') between 1 and 100) is not true then
    raise exception using errcode='22023',message='Resumo da sessao invalido.';
  end if;
  started := (p_session->>'started_utc')::timestamptz;
  ended := (p_session->>'ended_utc')::timestamptz;
  if started is null or ended is null or ended<started then
    raise exception using errcode='22023',message='Datas de sessao invalidas.';
  end if;
  if jsonb_typeof(p_session->'sample_counts') is distinct from 'object' then
    raise exception using errcode='22023',message='Contagem de amostras ausente.';
  end if;
  foreach sample_field in array array['head','controllers','left_tracked','right_tracked'] loop
    if ((p_session->'sample_counts'->>sample_field)::bigint between 0 and 10000000) is not true then
      raise exception using errcode='22023',message='Contagem de amostras invalida.';
    end if;
  end loop;
  if (p_session->'sample_counts'->>'left_tracked')::bigint>(p_session->'sample_counts'->>'controllers')::bigint
     or (p_session->'sample_counts'->>'right_tracked')::bigint>(p_session->'sample_counts'->>'controllers')::bigint then
    raise exception using errcode='22023',message='Contagem de rastreio invalida.';
  end if;

  -- Serializa retries concorrentes da mesma sessao; eventos nao sao reinseridos.
  perform pg_advisory_xact_lock(hashtextextended(session_code,0));
  select * into existing from public.sessions where session_id=session_code;
  if found then
    if existing.participant_code=participant and existing.uploaded_by=operator_id and existing.upload_digest=p_digest then
      return jsonb_build_object('session_id',session_code,'participant_code',participant,'digest',p_digest,'file_count',4,'event_count',jsonb_array_length(p_events),'status','already_uploaded');
    end if;
    raise exception using errcode='23505',message='UUID de sessao ja associado a outro conteudo ou responsavel.';
  end if;

  path_prefix := operator_id::text||'/'||participant||'/'||session_code||'/';
  foreach file_name in array array['Session.json','Head.csv','Controllers.csv','Events.csv'] loop
    expected_type := case file_name when 'Session.json' then 'session_json' when 'Head.csv' then 'head_csv' when 'Controllers.csv' then 'controllers_csv' else 'events_csv' end;
    expected_mime := case when file_name='Session.json' then 'application/json' else 'text/csv' end;
    select value into item from jsonb_array_elements(p_files)
      where value->>'storage_path'=path_prefix||file_name;
    if item is null or (item->>'file_type'=expected_type and item->>'bucket_id'='vr-telemetry'
      and item->>'content_type'=expected_mime and item->>'sha256' ~ '^[0-9a-f]{64}$'
      and (item->>'byte_size')::bigint between 1 and 67108864) is not true then
      raise exception using errcode='22023',message='Manifesto de arquivos invalido.';
    end if;
    if not exists(select 1 from storage.objects o where o.bucket_id='vr-telemetry' and o.name=path_prefix||file_name
      and (o.metadata->>'size')::bigint=(item->>'byte_size')::bigint) then
      raise exception using errcode='22023',message='Arquivo nao confirmado no Storage.';
    end if;
  end loop;

  insert into public.sessions(session_id,participant_code,started_utc,ended_utc,duration_seconds,attempts,correct_answers,errors,
    application_version,schema_version,coordinate_space,uploaded_by,upload_digest,uploaded_at,sample_counts)
  values(session_code,participant,started,ended,(p_session->>'duration_seconds')::real,(p_session->>'attempts')::integer,
    (p_session->>'correct_answers')::integer,(p_session->>'errors')::integer,p_session->>'application_version',
    (p_session->>'schema_version')::integer,p_session->>'coordinate_space',operator_id,p_digest,now(),p_session->'sample_counts');

  for event_item in select value from jsonb_array_elements(p_events) loop
    if (length(event_item->>'event_type') between 1 and 120 and length(coalesce(event_item->>'object_id',''))<=200
      and length(coalesce(event_item->>'details',''))<=4000 and (event_item->>'time_since_start')::numeric>=0
      and (event_item->>'time_since_start')::numeric <= (p_session->>'duration_seconds')::numeric+5) is not true then
      raise exception using errcode='22023',message='Evento invalido.';
    end if;
  end loop;
  for task_id in select distinct value->>'object_id' from jsonb_array_elements(p_events)
    where value->>'event_type' in ('task_correct','task_error') and length(coalesce(value->>'object_id',''))>0 loop
    insert into public.tasks(task_key,title) values(task_id,task_id) on conflict(task_key) do nothing;
  end loop;
  insert into public.session_events(session_id,event_index,time_since_start,event_type,object_id,details,task_key)
  select session_code,(ordinality-1)::integer,(value->>'time_since_start')::real,value->>'event_type',
    nullif(value->>'object_id',''),value->>'details',
    case when value->>'event_type' in ('task_correct','task_error') then nullif(value->>'object_id','') else null end
  from jsonb_array_elements(p_events) with ordinality;
  insert into public.session_files(session_id,file_type,bucket_id,storage_path,byte_size,sha256,content_type)
  select session_code,value->>'file_type','vr-telemetry',value->>'storage_path',(value->>'byte_size')::bigint,
    value->>'sha256',value->>'content_type' from jsonb_array_elements(p_files);
  return jsonb_build_object('session_id',session_code,'participant_code',participant,'digest',p_digest,'file_count',4,'event_count',jsonb_array_length(p_events),'status','uploaded');
end;
$$;
revoke all on function public.ingest_telemetry_session(jsonb,jsonb,jsonb,text) from public, anon;
grant execute on function public.ingest_telemetry_session(jsonb,jsonb,jsonb,text) to authenticated;
notify pgrst, 'reload schema';
commit;
