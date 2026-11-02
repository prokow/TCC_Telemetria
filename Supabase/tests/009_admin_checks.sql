-- Executar no SQL Editor DEPOIS da 009. Fixtures temporarias, sem upload/exclusao real de arquivos.
begin;
select set_config('admin_test.user',(select user_id::text from telemetry_private.operators where active and app_role='admin' order by user_id limit 1),true);
select set_config('request.jwt.claim.sub',current_setting('admin_test.user'),true);
select set_config('admin_test.person',replace(gen_random_uuid()::text,'-',''),true);
select set_config('admin_test.empty',replace(gen_random_uuid()::text,'-',''),true);
select set_config('admin_test.session',replace(gen_random_uuid()::text,'-',''),true);
insert into public.participants(participant_code,display_name) values
  (current_setting('admin_test.person'),'Fixture com sessao'),(current_setting('admin_test.empty'),'Fixture vazia');
insert into public.sessions(session_id,participant_code,started_utc,ended_utc,duration_seconds,attempts,correct_answers,errors)
values(current_setting('admin_test.session'),current_setting('admin_test.person'),now(),now(),60,3,2,1);
set local role authenticated;
do $$
declare result jsonb; overview record;
begin
  if not public.is_telemetry_admin() then raise exception 'FAIL: administrador nao reconhecido'; end if;
  result:=public.telemetry_account_access();
  if result->>'role'<>'admin' or result->>'user_id'<>current_setting('admin_test.user') then raise exception 'FAIL: identidade administrativa incorreta'; end if;
  select * into strict overview from public.admin_participant_overview where participant_code=current_setting('admin_test.person');
  if overview.session_count<>1 or overview.attempts<>3 or overview.correct_answers<>2 or overview.errors<>1 then raise exception 'FAIL: resumo incorreto'; end if;
  result:=public.admin_rename_participant(current_setting('admin_test.person'),'Fixture renomeada');
  if result->>'display_name'<>'Fixture renomeada' or result->>'participant_code'<>current_setting('admin_test.person') then raise exception 'FAIL: alteracao mudou o UUID'; end if;
  begin
    perform public.admin_delete_empty_participant(current_setting('admin_test.person'));
    raise exception 'FAIL: exclusao de historico permitida';
  exception when raise_exception then if SQLERRM<>'PARTICIPANT_HAS_TELEMETRY' then raise; end if; end;
  result:=public.admin_delete_empty_participant(current_setting('admin_test.empty'));
  if result->>'deleted'<>'true' or exists(select 1 from public.participants where participant_code=current_setting('admin_test.empty')) then raise exception 'FAIL: exclusao vazia nao confirmada'; end if;
  begin
    update telemetry_private.operators set app_role='admin';
    raise exception 'FAIL: cliente pode alterar privilegios';
  exception when insufficient_privilege then null; end;
  perform set_config('request.jwt.claim.sub','00000000-0000-0000-0000-000000000000',true);
  if public.is_telemetry_admin() then raise exception 'FAIL: usuario desconhecido virou administrador'; end if;
  if exists(select 1 from public.admin_participant_overview) then raise exception 'FAIL: resumo administrativo exposto'; end if;
  begin
    perform public.admin_rename_participant(current_setting('admin_test.person'),'Tentativa indevida');
    raise exception 'FAIL: usuario desconhecido editou participante';
  exception when insufficient_privilege then null; end;
  begin
    perform public.admin_delete_empty_participant(current_setting('admin_test.person'));
    raise exception 'FAIL: usuario desconhecido excluiu participante';
  exception when insufficient_privilege then null; end;
  if has_function_privilege('anon','public.admin_rename_participant(text,text)','EXECUTE')
     or has_function_privilege('anon','public.admin_delete_empty_participant(text)','EXECUTE') then
    raise exception 'FAIL: funcao administrativa liberada para anon';
  end if;
  raise notice 'PASS: administrador por UUID, resumo, edicao, exclusao limitada e bloqueio de autoelevacao.';
end $$;
reset role;
do $$ begin
  if (select count(*) from telemetry_private.admin_audit where participant_code in (current_setting('admin_test.person'),current_setting('admin_test.empty')))<>2 then
    raise exception 'FAIL: alteracoes sem auditoria';
  end if;
end $$;
rollback;
