-- Executar uma vez no SQL Editor, depois do schema modular 002 e do cadastro 003.
-- Leitura de telemetria herda os participantes que a conta ja pode consultar via RLS.
-- Nao exige colar outro UUID e nao libera upload, INSERT, UPDATE ou DELETE.
begin;

do $check$
begin
    if to_regclass('public.sessions') is null or to_regclass('public.session_events') is null
       or to_regclass('public.tasks') is null or to_regclass('public.session_files') is null
       or to_regclass('public.session_task_summary') is null then
        raise exception 'Execute primeiro o schema modular 002 (sessions, eventos, tasks, arquivos e view).';
    end if;
    if not exists (
        select 1 from pg_class c join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = 'public' and c.relname = 'participants' and c.relrowsecurity
    ) then
        raise exception 'A tabela participants precisa de RLS e das permissoes do cadastro 003.';
    end if;
end;
$check$;

grant usage on schema public to authenticated;
grant select (created_at) on public.participants to authenticated;
grant select on public.sessions, public.session_events, public.tasks, public.session_files to authenticated;
alter table public.sessions enable row level security;
alter table public.session_events enable row level security;
alter table public.tasks enable row level security;
alter table public.session_files enable row level security;

drop policy if exists sessions_read_visible_participant on public.sessions;
create policy sessions_read_visible_participant on public.sessions for select to authenticated
using (exists (select 1 from public.participants p where p.participant_code = sessions.participant_code));

drop policy if exists events_read_visible_session on public.session_events;
create policy events_read_visible_session on public.session_events for select to authenticated
using (exists (select 1 from public.sessions s where s.session_id = session_events.session_id));

drop policy if exists files_read_visible_session on public.session_files;
create policy files_read_visible_session on public.session_files for select to authenticated
using (exists (select 1 from public.sessions s where s.session_id = session_files.session_id));

drop policy if exists tasks_read_participant_operator on public.tasks;
create policy tasks_read_participant_operator on public.tasks for select to authenticated
using (exists (select 1 from public.participants));

alter view public.session_task_summary set (security_invoker = true);
grant select on public.session_task_summary to authenticated;

-- Contrato explicito: antes desta migracao o frontend apresenta "configurar acesso",
-- em vez de confundir falta de permissoes com um participante sem telemetria.
create or replace function public.participant_telemetry_access(p_participant_code text)
returns boolean language sql stable security invoker set search_path = ''
as $$
    select exists (
        select 1 from public.participants p
        where p.participant_code = p_participant_code
    );
$$;
revoke all on function public.participant_telemetry_access(text) from public, anon;
grant execute on function public.participant_telemetry_access(text) to authenticated;

commit;
