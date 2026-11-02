-- Teste transacional de banco. Executar no SQL Editor DEPOIS da migracao 007.
-- Cria apenas fixtures de metadados e faz ROLLBACK de tudo. Nao testa os bytes do Storage.
-- Nunca gravar fixtures diretamente em storage.objects fora desta transacao de teste.
begin;
select set_config('telemetry_test.operator', (select user_id::text from telemetry_private.operators where active order by user_id limit 1), true);
select set_config('request.jwt.claim.sub', current_setting('telemetry_test.operator'), true);
select set_config('telemetry_test.participant', replace(gen_random_uuid()::text,'-',''), true);
select set_config('telemetry_test.session', replace(gen_random_uuid()::text,'-',''), true);
insert into public.participants(participant_code,display_name)
values(current_setting('telemetry_test.participant'),'Fixture transacional de ingestao');
insert into storage.objects(bucket_id,name,metadata)
select 'vr-telemetry', current_setting('telemetry_test.operator')||'/'||current_setting('telemetry_test.participant')||'/'||current_setting('telemetry_test.session')||'/'||name,
       jsonb_build_object('size',42)
from unnest(array['Session.json','Head.csv','Controllers.csv','Events.csv']) as name;

set local role authenticated;
do $$
declare
  sid text := current_setting('telemetry_test.session');
  participant text := current_setting('telemetry_test.participant');
  prefix text := current_setting('telemetry_test.operator')||'/'||participant||'/'||sid||'/';
  summary jsonb;
  events jsonb := '[{"time_since_start":1,"event_type":"task_correct","object_id":"fixture_activity","details":"fixture"}]';
  files jsonb;
  result jsonb;
begin
  if not public.is_telemetry_operator() then raise exception 'FAIL: operador nao reconhecido'; end if;
  summary := jsonb_build_object('session_id',sid,'participant_code',participant,'started_utc','2026-11-01T10:00:00Z',
    'ended_utc','2026-11-01T10:01:00Z','duration_seconds',60,'attempts',1,'correct_answers',1,'errors',0,
    'application_version','fixture','schema_version',2,'coordinate_space','World',
    'sample_counts',jsonb_build_object('head',0,'controllers',0,'left_tracked',0,'right_tracked',0));
  select jsonb_agg(jsonb_build_object('file_type',case name when 'Session.json' then 'session_json' when 'Head.csv' then 'head_csv' when 'Controllers.csv' then 'controllers_csv' else 'events_csv' end,
    'bucket_id','vr-telemetry','storage_path',prefix||name,'byte_size',42,'sha256',repeat('a',64),
    'content_type',case when name='Session.json' then 'application/json' else 'text/csv' end)) into files
  from unnest(array['Session.json','Head.csv','Controllers.csv','Events.csv']) as name;
  result := public.ingest_telemetry_session(summary,events,files,repeat('b',64));
  if result->>'status'<>'uploaded' or result->>'participant_code'<>participant then raise exception 'FAIL: ACK invalido'; end if;
  result := public.ingest_telemetry_session(summary,events,files,repeat('b',64));
  if result->>'status'<>'already_uploaded' then raise exception 'FAIL: retry nao idempotente'; end if;
  if (select count(*) from public.session_events where session_id=sid)<>1 then raise exception 'FAIL: eventos duplicados'; end if;
  if (select count(*) from public.session_files where session_id=sid)<>4 then raise exception 'FAIL: manifesto incompleto'; end if;
  if (select uploaded_by::text from public.sessions where session_id=sid)<>current_setting('telemetry_test.operator') then raise exception 'FAIL: dono da sessao incorreto'; end if;
  begin
    perform public.ingest_telemetry_session(summary,events,files,repeat('c',64));
    raise exception 'FAIL: conteudo diferente reutilizou o UUID';
  exception when unique_violation then null; end;
  begin
    insert into public.sessions(session_id,participant_code,started_utc) values(replace(gen_random_uuid()::text,'-',''),participant,now());
    raise exception 'FAIL: escrita direta permitida';
  exception when insufficient_privilege then null; end;
  perform set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000000',true);
  if public.is_telemetry_operator() then raise exception 'FAIL: desconhecido autorizado'; end if;
  begin
    perform public.ingest_telemetry_session(summary,events,files,repeat('b',64));
    raise exception 'FAIL: ingestao aceitou desconhecido';
  exception when insufficient_privilege then null; end;
  perform set_config('request.jwt.claim.sub',current_setting('telemetry_test.operator'),true);
  raise notice 'PASS: ingestao atomica, retry, UUID, quatro arquivos e restricoes de escrita.';
end $$;
rollback;
