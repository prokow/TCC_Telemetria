-- Executar no SQL Editor do MESMO projeto usado pelo Unity.
-- Substituir COLE_UUID_DO_RESPONSAVEL pelo UUID de Authentication > Users.
-- Exemplo válido de UUID: 00000000-0000-0000-0000-000000000000 (não usar esse exemplo).
-- O script apenas autoriza consulta e cadastro de participantes. Não libera telemetria/sessions.
begin;

do $migration$
declare
    operator_id uuid := 'COLE_UUID_DO_RESPONSAVEL'::uuid;
begin
    if not exists (select 1 from auth.users where id = operator_id) then
        raise exception 'O responsável não existe em Authentication neste projeto.';
    end if;

    -- Depois da migracao 007, trocar o responsavel tambem revoga o antigo uploader.
    if to_regclass('telemetry_private.operators') is not null then
        update telemetry_private.operators set active = false where user_id <> operator_id;
        insert into telemetry_private.operators(user_id, active) values(operator_id, true)
        on conflict(user_id) do update set active = true;
    end if;

    alter table public.participants enable row level security;
    grant usage on schema public to authenticated;
    grant select (participant_code, display_name) on public.participants to authenticated;
    grant insert (participant_code, display_name) on public.participants to authenticated;

    drop policy if exists participants_read_menu_operator on public.participants;
    execute format(
        'create policy participants_read_menu_operator on public.participants for select to authenticated using ((select auth.uid()) = %L::uuid)',
        operator_id::text
    );
    drop policy if exists participants_insert_web_operator on public.participants;
    execute format(
        $policy$create policy participants_insert_web_operator
        on public.participants for insert to authenticated
        with check (
            (select auth.uid()) = %L::uuid
            and participant_code ~ '^[0-9a-f]{32}$'
            and display_name is not null
            and display_name = btrim(display_name)
            and char_length(display_name) between 1 and 120
            and display_name !~ '[[:cntrl:]]'
        )$policy$,
        operator_id::text
    );
end;
$migration$;

commit;
