-- Somente leitura. Executar no SQL Editor apos aplicar 007.
select id as bucket, public as publico, file_size_limit, allowed_mime_types
from storage.buckets where id='vr-telemetry';

select p.oid::regprocedure as funcao, p.prosecdef as security_definer,
  has_function_privilege('authenticated',p.oid,'EXECUTE') as authenticated_executa,
  has_function_privilege('anon',p.oid,'EXECUTE') as anon_executa
from pg_proc p join pg_namespace n on n.oid=p.pronamespace
where n.nspname='public' and p.proname in ('is_telemetry_operator','ingest_telemetry_session','participant_telemetry_access');

select user_id as uuid_authentication, active as autorizado from telemetry_private.operators;

select session_id as uuid_sessao, participant_code as uuid_participante, uploaded_by as uuid_responsavel,
  uploaded_at, attempts, correct_answers, errors, sample_counts,
  (select count(*) from public.session_files f where f.session_id=s.session_id) as arquivos_indexados
from public.sessions s where upload_digest is not null
order by uploaded_at desc limit 10;

select policyname,cmd,roles from pg_policies
where schemaname='storage' and tablename='objects'
  and policyname in ('telemetry_operator_upload','telemetry_operator_download');
