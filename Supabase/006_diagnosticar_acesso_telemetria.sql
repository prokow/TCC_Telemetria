-- Somente leitura: executar no SQL Editor do mesmo projeto do frontend.
-- Nao consulta nomes, sessoes, senhas ou tokens dos participantes.
-- O SQL Editor usa uma identidade diferente do navegador: estes resultados
-- verificam os grants e as politicas, nao simulam o JWT da pessoa conectada.

select has_schema_privilege('authenticated', 'public', 'USAGE') as acesso_ao_schema;

with expected(table_name, column_name) as (
    values
        ('participants', 'participant_code'), ('participants', 'display_name'), ('participants', 'created_at'),
        ('sessions', 'session_id'), ('sessions', 'participant_code'), ('sessions', 'started_utc'),
        ('sessions', 'ended_utc'), ('sessions', 'duration_seconds'), ('sessions', 'attempts'),
        ('sessions', 'correct_answers'), ('sessions', 'errors'), ('sessions', 'application_version'),
        ('sessions', 'schema_version'), ('sessions', 'coordinate_space'),
        ('session_events', 'id'), ('session_events', 'session_id'), ('session_events', 'time_since_start'),
        ('session_events', 'event_type'), ('session_events', 'object_id'), ('session_events', 'details'),
        ('session_events', 'task_key'), ('session_files', 'id'), ('session_files', 'session_id'),
        ('session_files', 'file_type'), ('session_files', 'bucket_id'), ('session_files', 'storage_path'),
        ('session_files', 'byte_size'), ('session_files', 'uploaded_at'),
        ('tasks', 'task_key'), ('tasks', 'title')
)
select e.table_name as tabela, e.column_name as coluna,
       c.oid is not null as tabela_existe,
       a.attnum is not null as coluna_existe,
       c.relrowsecurity as rls_ativo,
       case when a.attnum is not null
            then has_column_privilege('authenticated', c.oid, a.attnum, 'SELECT')
            else null end as authenticated_pode_ler
from expected e
left join pg_namespace n on n.nspname = 'public'
left join pg_class c on c.relnamespace = n.oid and c.relname = e.table_name
left join pg_attribute a on a.attrelid = c.oid and a.attname = e.column_name
                       and a.attnum > 0 and not a.attisdropped
order by e.table_name, e.column_name;

select
    to_regprocedure('public.participant_telemetry_access(text)') is not null as funcao_005_existe,
    case when to_regprocedure('public.participant_telemetry_access(text)') is not null
         then has_function_privilege('authenticated', to_regprocedure('public.participant_telemetry_access(text)'), 'EXECUTE')
         else null end as authenticated_pode_verificar_acesso;

select tablename as tabela, policyname as politica, cmd as operacao,
       roles, qual as condicao_leitura, with_check as condicao_gravacao
from pg_policies
where schemaname = 'public'
  and tablename in ('participants', 'sessions', 'session_events', 'session_files', 'tasks')
order by tablename, policyname;
