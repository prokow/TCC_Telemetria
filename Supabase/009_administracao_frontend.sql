-- Depois de 003/005/007. Execute inteiro no SQL Editor, sem repetir o 002.
-- Usa o UUID do unico responsavel ativo ja autorizado; nao transforma clientes em service_role.
begin;
do $$ begin
  if to_regclass('telemetry_private.operators') is null
     or to_regprocedure('public.ingest_telemetry_session(jsonb,jsonb,jsonb,text)') is null then
    raise exception 'Execute primeiro 003, 005 e 007 no mesmo projeto Supabase.';
  end if;
end $$;

alter table telemetry_private.operators add column if not exists app_role text not null default 'operator';
do $$ begin
  if not exists(select 1 from pg_constraint where conrelid='telemetry_private.operators'::regclass and conname='operators_app_role_check') then
    alter table telemetry_private.operators add constraint operators_app_role_check check(app_role in ('admin','operator'));
  end if;
  if not exists(select 1 from telemetry_private.operators where active and app_role='admin') then
    if (select count(*) from telemetry_private.operators where active) <> 1 then
      raise exception 'Nao ha um unico responsavel ativo. Confira a migracao 003 e escolha explicitamente o UUID administrador no SQL Editor antes de aplicar esta migracao.';
    end if;
    update telemetry_private.operators set app_role='admin' where active;
  end if;
end $$;
revoke all on telemetry_private.operators from public, anon, authenticated;

create or replace function public.is_telemetry_admin()
returns boolean language sql stable security definer set search_path=''
as $$ select auth.uid() is not null and exists (
  select 1 from telemetry_private.operators where user_id=auth.uid() and active and app_role='admin'
); $$;
revoke all on function public.is_telemetry_admin() from public, anon;
grant execute on function public.is_telemetry_admin() to authenticated;

create or replace function public.telemetry_account_access()
returns jsonb language sql stable security definer set search_path=''
as $$ select jsonb_build_object('user_id',auth.uid(),'is_admin',public.is_telemetry_admin(),
  'role',coalesce((select app_role from telemetry_private.operators where user_id=auth.uid() and active),'none')); $$;
revoke all on function public.telemetry_account_access() from public, anon;
grant execute on function public.telemetry_account_access() to authenticated;

-- Alteracoes administrativas usam RPCs com validacao e registro; UUIDs da pesquisa permanecem imutaveis.
create table if not exists telemetry_private.admin_audit (
  id bigint generated always as identity primary key,
  actor_id uuid not null,
  action text not null,
  participant_code text not null,
  before_value jsonb,
  after_value jsonb,
  created_at timestamptz not null default now()
);
revoke all on telemetry_private.admin_audit from public, anon, authenticated;
revoke all on sequence telemetry_private.admin_audit_id_seq from public, anon, authenticated;

create or replace function public.admin_rename_participant(p_participant_code text, p_display_name text)
returns jsonb language plpgsql security definer set search_path=''
as $$
declare previous public.participants%rowtype; normalized text := btrim(p_display_name);
begin
  if not public.is_telemetry_admin() then raise exception using errcode='42501',message='Administrador nao autorizado.'; end if;
  if (p_participant_code ~ '^[0-9a-f]{32}$' and char_length(normalized) between 1 and 120 and normalized !~ '[[:cntrl:]]') is not true then
    raise exception using errcode='22023',message='Nome ou UUID invalido.';
  end if;
  select * into previous from public.participants where participant_code=p_participant_code for update;
  if not found then raise exception using errcode='P0002',message='PARTICIPANT_NOT_FOUND'; end if;
  update public.participants set display_name=normalized where participant_code=p_participant_code;
  if previous.display_name is distinct from normalized then
    insert into telemetry_private.admin_audit(actor_id,action,participant_code,before_value,after_value)
    values(auth.uid(),'rename_participant',p_participant_code,jsonb_build_object('display_name',previous.display_name),jsonb_build_object('display_name',normalized));
  end if;
  return jsonb_build_object('participant_code',p_participant_code,'display_name',normalized);
end; $$;
revoke all on function public.admin_rename_participant(text,text) from public, anon;
grant execute on function public.admin_rename_participant(text,text) to authenticated;

create or replace function public.admin_delete_empty_participant(p_participant_code text)
returns jsonb language plpgsql security definer set search_path=''
as $$
declare previous public.participants%rowtype;
begin
  if not public.is_telemetry_admin() then raise exception using errcode='42501',message='Administrador nao autorizado.'; end if;
  if (p_participant_code ~ '^[0-9a-f]{32}$') is not true then raise exception using errcode='22023',message='UUID invalido.'; end if;
  select * into previous from public.participants where participant_code=p_participant_code for update;
  if not found then raise exception using errcode='P0002',message='PARTICIPANT_NOT_FOUND'; end if;
  if exists(select 1 from public.sessions where participant_code=p_participant_code)
     or exists(select 1 from storage.objects where bucket_id='vr-telemetry' and split_part(name,'/',2)=p_participant_code) then
    raise exception using errcode='P0001',message='PARTICIPANT_HAS_TELEMETRY';
  end if;
  delete from public.participants where participant_code=p_participant_code;
  insert into telemetry_private.admin_audit(actor_id,action,participant_code,before_value)
  values(auth.uid(),'delete_empty_participant',p_participant_code,jsonb_build_object('display_name',previous.display_name));
  return jsonb_build_object('participant_code',p_participant_code,'deleted',true);
end; $$;
revoke all on function public.admin_delete_empty_participant(text) from public, anon;
grant execute on function public.admin_delete_empty_participant(text) to authenticated;

-- O cadastro continua pelas politicas existentes; leitura de todas as linhas para o administrador.
grant select (participant_code,display_name,created_at), insert (participant_code,display_name) on public.participants to authenticated;
drop policy if exists participants_read_admin on public.participants;
create policy participants_read_admin on public.participants for select to authenticated using ((select public.is_telemetry_admin()));
drop policy if exists participants_insert_admin on public.participants;
create policy participants_insert_admin on public.participants for insert to authenticated
with check ((select public.is_telemetry_admin()) and participant_code ~ '^[0-9a-f]{32}$'
  and display_name is not null and display_name=btrim(display_name) and char_length(display_name) between 1 and 120 and display_name !~ '[[:cntrl:]]');

create or replace view public.admin_participant_overview with (security_invoker=true) as
select p.participant_code,p.display_name,p.created_at,
  count(s.session_id)::bigint as session_count,
  coalesce(sum(s.duration_seconds::double precision),0)::double precision as duration_seconds,
  coalesce(sum(s.attempts),0)::bigint as attempts,
  coalesce(sum(s.correct_answers),0)::bigint as correct_answers,
  coalesce(sum(s.errors),0)::bigint as errors
from public.participants p left join public.sessions s on s.participant_code=p.participant_code
where (select public.is_telemetry_admin())
group by p.participant_code,p.display_name,p.created_at;
revoke all on public.admin_participant_overview from public, anon;
grant select on public.admin_participant_overview to authenticated;
notify pgrst, 'reload schema';
commit;

-- Conferir no SQL Editor (nao compartilhar senha/chaves):
-- select user_id, active, app_role from telemetry_private.operators;
-- Para adicionar outro administrador ja cadastrado em Authentication:
-- insert into telemetry_private.operators(user_id,active,app_role) values ('UUID_AUTHENTICATION',true,'admin')
-- on conflict(user_id) do update set active=true,app_role='admin';
