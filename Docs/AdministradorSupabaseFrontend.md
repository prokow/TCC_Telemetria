# Administrador por UUID no Supabase e no frontend

A conta ja autorizada pode se tornar administradora da aplicacao. E o UUID de **Authentication > Users**, nao o `participant_code` de uma pessoa cadastrada nem o UUID de uma sessao. O papel nao e superusuario PostgreSQL e nao usa chave service_role no navegador ou no Unity.

## Aplicar no banco

Nao execute novamente o 002 depois de configurar permissoes: ele revoga acessos de clientes. Ele e a base das tabelas, e as migracoes seguintes acrescentam recursos.

1. Se voce aplicou somente o schema 002, execute `Supabase/003_frontend_participants.sql`, preenchendo o UUID de Authentication do responsavel uma vez, e depois `005_participant_telemetry_analysis.sql` e `007_telemetry_storage_ingestion.sql`.
2. Se 003/005/007 ja foram aplicadas, avance diretamente para `Supabase/009_administracao_frontend.sql`. Execute o arquivo **inteiro** no SQL Editor do mesmo projeto.
3. A 009 usa o unico responsavel ativo de `telemetry_private.operators` e atribui `app_role='admin'`. Nao precisa informar outro UUID. Se houver zero ou varios responsaveis ativos e nenhum administrador, o script interrompe em vez de escolher uma conta por suposicao.
4. Confira no SQL Editor:

```sql
select user_id as uuid_authentication, active, app_role
from telemetry_private.operators;
```

O UUID desejado deve estar com `active=true` e `app_role='admin'`. Nao altere `auth.users.is_super_admin` nem coloque esse papel em user_metadata controlado pelo cliente.

Caso precise escolher explicitamente outro administrador existente em Authentication, aplique pelo SQL Editor, substituindo somente o UUID:

```sql
insert into telemetry_private.operators(user_id,active,app_role)
values ('UUID_REAL_DE_AUTHENTICATION',true,'admin')
on conflict(user_id) do update set active=true,app_role='admin';
```

Essa operacao autoriza a conta adicional; nao revoga administradores existentes. Para retirar somente a funcao administrativa, altere `app_role` para `operator`. Para retirar tambem o acesso de operador/Unity, altere `active` para `false`.

## O que cada identidade pode fazer

| Identidade | Acesso |
| --- | --- |
| Sem login / anon | Sem acesso as tabelas e arquivos privados deste fluxo |
| Authentication sem autorizacao em operators | Sem acesso administrativo; fazer login sozinho nao autoriza gerenciamento |
| Operador ativo | Fluxo existente de participantes/Unity e ingestao validada da telemetria |
| Administrador ativo | Cadastro, consulta consolidada, analise/download dos arquivos, renomeacao e exclusao de cadastro vazio |

O frontend administrativo exige a confirmacao de `telemetry_account_access` no banco. Nao decide o privilegio a partir de um UUID hardcoded ou de uma variavel do JavaScript. Todas as RPCs administrativas conferem `auth.uid()` e o papel ativo novamente.

Renomear conserva o UUID e as relacoes com sessoes/eventos/arquivos. Excluir so e permitido quando nao existem sessoes nem objetos conhecidos do participante no bucket `vr-telemetry`; exclusoes e coletas concorrentes devem ser evitadas. A exclusao nao e um mecanismo para apagar historico de pesquisa. A 009 nao concede escrita direta nos resultados brutos. Alteracoes de nome/exclusoes vazias ficam registradas em `telemetry_private.admin_audit`, que nao fica exposta a clientes.

Administrar dados nao equivale a sobrescrever CSVs ou editar contadores independentemente dos eventos. Para correcao de medicoes ou exclusao completa de historico, e necessario um fluxo especifico que mantenha consistentes SQL, hashes e Storage. Nao delete linhas de `storage.objects` manualmente: excluir metadados por SQL nao remove os bytes armazenados, conforme a [documentacao do Supabase Storage](https://supabase.com/docs/guides/storage/management/delete-objects).

## Usar o frontend

1. Reinicie o servidor local na pasta `Web/participantes-vr`:

```powershell
node --preserve-symlinks-main --preserve-symlinks serve.mjs
```

2. Abra `http://127.0.0.1:4173`, recarregue com Ctrl+F5 e entre com o email/senha da conta administradora. A chave publica ja configurada continua igual.
3. O painel confirma **Administrador** e mostra seu UUID. A lista agora apresenta sessoes, tentativas, acertos e erros acumulados por participante. Uma pessoa sem sessoes apresenta zero apenas depois da consulta confirmada.
4. **Cadastrar participante** cria o nome com UUID proprio. **Editar cadastro** permite renomear; o UUID nao muda. A exclusao exige digitar o UUID completo e e bloqueada se houver historico ou arquivos.
5. **Ver analise** apresenta duracao, taxa de acerto, historico, atividades e eventos. Em **Detalhes**, os CSVs/JSON privados permitem download e analise, com conferencia de UUID, tamanho e SHA-256.
6. Na Unity, o menu continua consultando os mesmos participantes. Renomear aparece na proxima atualizacao da lista. O frontend pode ser fechado durante a experiencia.

Os CSVs precisam ter sido enviados pelo Unity. Essa migracao nao recupera automaticamente arquivos que ficaram apenas no headset/computador; finalize a coleta com o uploader configurado pela 007 e aguarde a confirmacao no Console. Os arquivos sao `Session.json`, `Head.csv`, `Controllers.csv` e `Events.csv`, associados aos UUIDs de participante/sessao.

## Validacao

Os testes web cobrem identidade administrativa, recusa de usuarios nao autorizados, resumo paginado, alteracao por UUID, confirmacao da exclusao, dados com historico, cancelamento, respostas atrasadas, expiracao e integridade dos arquivos. Resultado local: **67 testes web aprovados, sem falhas ou testes pulados**, incluindo o Worker lendo JSON/CSVs gerados pelo C#, e **338 verificacoes C# aprovadas**. Execute primeiro `Tools/ParticipantIntegrationChecks/Run.ps1` para gerar os arquivos reais C# usados no teste de interoperabilidade, depois `npm test` na pasta do frontend.

`Supabase/tests/009_admin_checks.sql` e um teste transacional para executar no SQL Editor depois da 009. Usa fixtures e termina com ROLLBACK. Se houver falha antes do final, execute `rollback;` antes de outras consultas.

A migracao e o teste SQL nao foram executados no banco remoto nesta entrega: nao ha conexao administrativa ao Supabase neste ambiente. Testes HTTP simulados nao comprovam permissoes aplicadas no seu projeto. O teste visual no navegador tambem permanece pendente. A tentativa de publicar pelo helper Sites encontrou EPERM ao acessar o diretorio de perfil do Windows; o frontend atualizado esta disponivel localmente/no pacote, sem URL de publicacao confirmada.
