-- Somente leitura. Executar no SQL Editor do projeto usado pelo frontend e Unity.
-- Nao modifica tabelas, dados, usuarios ou politicas.

select
    has_schema_privilege('authenticated', 'public', 'USAGE') as schema_usage,
    has_column_privilege('authenticated', 'public.participants', 'participant_code', 'SELECT') as pode_ler_codigo,
    has_column_privilege('authenticated', 'public.participants', 'display_name', 'SELECT') as pode_ler_nome,
    has_column_privilege('authenticated', 'public.participants', 'participant_code', 'INSERT') as pode_inserir_codigo,
    has_column_privilege('authenticated', 'public.participants', 'display_name', 'INSERT') as pode_inserir_nome;

select relrowsecurity as rls_ativo
from pg_class
where oid = 'public.participants'::regclass;

select policyname, cmd, roles, qual as condicao_leitura, with_check as condicao_cadastro
from pg_policies
where schemaname = 'public' and tablename = 'participants'
order by policyname;
