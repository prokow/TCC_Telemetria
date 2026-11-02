# Envio automatico da telemetria ao Supabase

O fluxo modular agora conserva o backup local e envia automaticamente os arquivos finalizados para o Supabase. Usa somente Supabase, sem Firebase. O menu e as referencias XR existentes foram preservados.

## Ativar no seu projeto

1. No Supabase SQL Editor do projeto configurado no Unity/frontend, execute **todo** o arquivo `Supabase/007_telemetry_storage_ingestion.sql`. O schema modular 002 e as migracoes 003/005 devem existir. A 007 aproveita o UUID ja autorizado pela politica de participantes; nao exige preencher outro UUID.
2. Aguarde o sucesso da consulta. Se ela falhar, a transacao nao deve ser considerada aplicada: confira o erro antes de continuar. Nao use chave administrativa no Unity.
3. Abra MainMenu no Unity, aguarde a compilacao e de Play. O login automatico existente continua usando a configuracao ja preenchida. Escolha um participante cadastrado e inicie a experiencia.
4. Termine a ultima quest. A coleta e salva localmente e a fila comeca o envio em segundo plano. O Console mostra `[SupabaseTelemetry] Sessao <UUID> confirmada no Supabase (4 arquivos).`
5. No frontend, reinicie o servidor Node e recarregue com Ctrl+F5. Entre, abra **Ver analise**, clique em **Atualizar banco** e escolha **Detalhes** da sessao. Os arquivos apresentam **Baixar arquivo**, **Ver amostras do CSV** ou **Analisar JSON do Storage**.

Nao e necessario adicionar componentes no Inspector: o servico persistente `SupabaseTelemetrySync` e criado apos uma autenticacao bem-sucedida no fluxo atual. Ele permanece disponivel em SampleScene e ao retornar ao menu. O ContextMenu desse componente possui **Reenviar sessoes pendentes agora**.

## Quais UUIDs sao utilizados

| Identificador | Uso |
| --- | --- |
| `participant_code`, GUID N de 32 caracteres | Cadastro real do participante, FK das sessoes e duas primeiras colunas de todos os CSVs |
| `session_id`, GUID N de 32 caracteres | Execucao individual; PK da sessao, FK dos eventos e arquivos, segunda coluna dos CSVs |
| UUID de Authentication do responsavel | Autorizacao, dono do envio e primeiro segmento do caminho no Storage |

O nome do participante e apenas exibicao. Pessoas com o mesmo nome continuam separadas pelo UUID. O UUID da sessao e criado pelo contexto existente, reutilizado em cada retry e nunca substituido para contornar uma falha de rede.

Cada objeto usa um caminho deterministico no bucket **privado** `vr-telemetry`:

```text
<uuid-responsavel>/<uuid-participante>/<uuid-sessao>/Session.json
<uuid-responsavel>/<uuid-participante>/<uuid-sessao>/Head.csv
<uuid-responsavel>/<uuid-participante>/<uuid-sessao>/Controllers.csv
<uuid-responsavel>/<uuid-participante>/<uuid-sessao>/Events.csv
```

Os CSVs comecam com `ParticipantCode,SessionId`. O JSON tambem conserva o responsavel e o projeto presentes no inicio da coleta. A fila recusa o envio de uma coleta vinculada a outro responsavel ou projeto. Arquivos legados que nao possuiam esses campos sao vinculados na primeira tentativa ao projeto e ao responsavel atuais, e so seguem se o UUID do participante ja estiver cadastrado e autorizado. O uploader nao cria participantes ficticios automaticamente.

## Como a confirmacao funciona

1. Gravar `Session.json`, os CSVs e o marcador `Ready.json` em `Application.persistentDataPath/Sessoes_Pendentes/<session_id>`.
2. Conferir identidade, contadores e formatos, e calcular SHA-256 dos arquivos derivados do JSON canonico.
3. Consultar se o participante esta disponivel para a conta conectada.
4. Enviar os quatro objetos com `x-upsert: false`. Se o caminho ja existir, baixar de forma autenticada e comparar o hash; nunca sobrescrever silenciosamente outro conteudo.
5. Chamar a RPC autenticada `ingest_telemetry_session`. Ela valida permissao, UUIDs, arquivos no Storage, contagens e eventos, e grava `sessions`, `session_events` e `session_files` em uma transacao.
6. Conferir se o ACK corresponde aos UUIDs, digest e quantidades esperados; somente depois gravar `Status: Uploaded` em `UploadState.json`.

O hash do lote e os UUIDs impedem que uma resposta perdida gere outra sessao. Um lock transacional serializa ingestao concorrente da mesma sessao. Repetir a mesma ingestao retorna `already_uploaded`, sem inserir eventos novamente. Reutilizar o UUID com outro conteudo ou responsavel e recusado.

Storage e PostgreSQL sao confirmados separadamente. Se o Storage aceitar os objetos mas a RPC falhar, os objetos permanecem privados e a fila tenta completar a etapa SQL depois. O frontend lista os arquivos apenas quando o indice de sessao foi gravado.

## Estado da fila e recuperacao

O manifesto `UploadState.json` registra projeto, UUIDs, digest, quantidade de arquivos confirmados, tentativas, proxima tentativa e uma mensagem de erro sanitizada. Ele nao contem senha, JWT ou refresh token.

- `Pending`: aguarda envio ou nova tentativa.
- `Uploading`: tentativa em andamento; um reinicio retoma o processamento.
- `Uploaded`: quatro arquivos e registros SQL confirmados.
- `ReviewRequired`: dados invalidos ou conflito de conteudo; exige conferir os arquivos antes de forcar nova tentativa.

O reenvio usa espera progressiva ate cinco minutos, timeout de rede e cancelamento. O processamento e serializado. Um arquivo com erro nao deve apagar os demais registros da fila. Os backups locais continuam presentes depois do envio; o nome `Sessoes_Pendentes` e mantido para compatibilidade, e o manifesto informa o estado real.

O menu e o uploader compartilham a mesma autorizacao renovavel, com renovacoes serializadas. A autorizacao persiste entre cenas e nao depende do GameObject do menu. O armazenamento de refresh token continua sendo o provedor Windows existente; a execucao direta em APK Android/Quest ainda exige um provedor protegido para essa plataforma.

Uma coleta iniciada isoladamente em SampleScene sem participante cadastrado pode continuar gerando arquivos de teste locais. O envio e bloqueado quando o UUID nao esta no cadastro; esses arquivos nao aparecem como sessoes reais no frontend.

## Seguranca no Supabase

A migracao 007 cria `telemetry_private.operators`, inacessivel aos clientes, e uma funcao que verifica o UUID autenticado. Os operadores iniciais sao obtidos da politica nomeada criada pela 003. O bucket e privado, com upload para caminhos do responsavel autenticado e participantes cadastrados. Downloads sao autenticados; a equipe autorizada pode consultar os arquivos publicados das sessoes que suas politicas de leitura permitem visualizar.

A escrita direta nas tabelas de telemetria permanece revogada para clientes. A RPC usa `security definer` com `search_path` vazio, valida explicitamente o responsavel e os dados e grava tudo em transacao. As permissoes de cadastro de participantes sao preservadas. Depois da 007, a migracao 003 tambem atualiza o cadastro privado de operadores ao trocar o responsavel, para revogar o uploader antigo.

Para desativar explicitamente um operador pelo SQL Editor:

```sql
update telemetry_private.operators
set active = false
where user_id = 'UUID_DE_AUTHENTICATION_DO_RESPONSAVEL';
```

Nenhuma operacao usa `service_role` no Unity ou frontend. A Publishable key identifica a aplicacao e o JWT autentica o responsavel. A classe legada `SupabaseManager` nao e usada pelo novo fluxo.

## Analise dos arquivos no frontend

O painel mantem os resumos e eventos do banco. Contagens de amostras e rastreio dos controles sao recebidas em `sessions.sample_counts`. A consulta a `session_files` recupera caminho, tamanho e SHA-256.

Antes de baixar, o frontend confere bucket, nome do arquivo e UUIDs do caminho. Faz GET autenticado ao Storage, verifica tamanho e SHA-256 e entao libera o download ou a analise no Worker. A previa de CSV confere os UUIDs em **cada linha**, trata aspas, virgulas e quebras de linha e mostra os primeiros 200 registros, com o total encontrado. O download permite consultar o arquivo completo. Nomes e descricoes sao exibidos como texto.

Limites desta etapa: JSON ate 32 MB; cada CSV ate 64 MB; 10.000 eventos por sessao; duracao ate sete dias. O limite efetivo tambem depende da configuracao do seu projeto Supabase. Arquivos fora dos limites sao mantidos localmente e precisam de revisao. Para pesquisas maiores, sera necessario evoluir para particionamento/resumable upload e leitura em blocos.

## Scripts e responsabilidades

| Arquivo | Responsabilidade |
| --- | --- |
| `TelemetryCsvEncoder.cs` | CSV deterministico, UUIDs nas linhas e escape de campos de texto |
| `TelemetryUploadPackage.cs` | Validar e preparar resumo, eventos, arquivos, hashes e digest |
| `TelemetryUploadStore.cs` | Fila no disco, caminhos por UUID e manifesto atomico |
| `TelemetryUploadClient.cs` | Protocolo de upload, verificacao de conflito e ACK de ingestao |
| `TelemetryUploadProcessor.cs` | Serializacao, retomada, vinculo de origem/responsavel e backoff |
| `UnityTelemetryUploadTransport.cs` | HTTPS via UnityWebRequest, JWT, timeout e cancelamento |
| `TelemetrySyncController.cs` | Servico persistente criado automaticamente e integracao entre cenas |
| `SharedSupabaseSession.cs` | Token de acesso compartilhado e renovacoes serializadas |
| `telemetry-csv.js` / `telemetry-worker.js` | Analise de CSV/JSON fora da thread principal do navegador |

## Testes e limites da verificacao

Os testes C# compilam as fontes reais com as bibliotecas Unity e executam a fila com um transporte simulado. Cobrem envio completo, falha parcial, reinicio, backoff, timeout apos commit, retry sem duplicacao, conflito de hash, ausencia de migracao, 401, UUID nao cadastrado, troca de projeto/responsavel e renovacao compartilhada. Os testes web incluem downloads privados, integridade, caminhos, CSVs e o Worker lendo os quatro arquivos reais gerados pelo teste C#.

Resultado local desta entrega: **338 verificacoes C#**, **55 testes web** e **13 verificacoes da associacao quests/coleta** aprovadas. O verificador antigo `CheckScene.ps1` encontrou uma divergencia no layout existente: exige `m_Spacing: 12`, mas o Canvas atual usa `5`. A cena nao foi alterada para satisfazer esse valor fixo. Esse resultado nao confirma nem reprova a interacao visual em VR, que exige verificacao no Editor/headset.

Execute primeiro:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ParticipantIntegrationChecks\Run.ps1
```

Depois, em `Web/participantes-vr`:

```powershell
node --preserve-symlinks-main --preserve-symlinks --test tests/client.test.mjs tests/static.test.mjs tests/telemetry.test.mjs tests/analysis-ui.test.mjs tests/worker.test.mjs tests/server.test.mjs tests/storage.test.mjs
```

`Supabase/tests/007_ingestion_checks.sql` fornece um teste transacional para executar no SQL Editor depois da migracao. Ele verifica a RPC, repeticao, UUIDs, escrita direta negada e responsavel desconhecido; faz ROLLBACK das fixtures. Os objetos de Storage desse teste sao somente metadados temporarios, sem upload de bytes. Se o teste falhar antes do final, execute `rollback;` antes de outras consultas.

`Supabase/008_verificar_configuracao_envio.sql` e um diagnostico somente de leitura: mostra se o bucket e privado, as permissoes das funcoes, os UUIDs dos responsaveis autorizados e os ultimos envios indexados. Uma sessao com quatro arquivos indexados e a mensagem de confirmacao do uploader permite conferir o fluxo depois da coleta real.

O ambiente desta entrega nao possui conexao administrativa ao seu Supabase nem PostgreSQL local, portanto a migracao e esse teste SQL **nao foram executados no seu banco**. O upload real exige aplicar a 007 e concluir uma sessao no Unity. O Play Mode em batch tinha uma falha de licenciamento no ambiente; a verificacao visual/VR e a confirmacao com a rede real permanecem pendentes. Os testes locais nao devem ser interpretados como prova de um upload ja realizado na sua conta.
