# Circuitos VR — participantes

Frontend estático para o responsável cadastrar participantes e analisar suas sessões no mesmo Supabase usado pelo Unity. Sem dependências de instalação ou serviço administrativo embutido.

## Administrador por UUID

A versão atual exige a migração `Supabase/009_administracao_frontend.sql`, depois de 003/005/007. Ela promove o único responsável ativo já autorizado ao papel de administrador da aplicação, vinculado ao UUID de Authentication. O frontend confirma esse papel no banco antes de abrir o painel. A lista mostra sessões, tentativas, acertos e erros por participante; **Editar cadastro** permite renomear e excluir cadastros sem sessões/arquivos. Os CSVs privados continuam disponíveis em **Ver análise > Detalhes**. O guia completo é `Docs/AdministradorSupabaseFrontend.md` no projeto e `GUIA_ADMINISTRADOR.md` no pacote. Não execute novamente o 002 para atualizar permissões já configuradas.

## Análise de telemetria por participante

Atualizacao do envio: aplique tambem `Supabase/007_telemetry_storage_ingestion.sql`. O Unity passa a enviar automaticamente JSON/CSVs finalizados e persistir sessoes/eventos por UUID. Em Detalhes, os arquivos privados podem ser baixados e os CSVs analisados no navegador, apos conferir UUIDs, tamanho e SHA-256. O guia completo esta em `Docs/EnvioTelemetriaSupabase.md` no projeto Unity e `GUIA_ENVIO_SUPABASE.md` no pacote.

Em **Participantes cadastrados**, clique em **Ver análise**. A tela apresenta nome e UUID, data de cadastro, total de sessões, tempo registrado, tentativas, acertos, erros e taxa de acerto ponderada pelo total de tentativas. Também há evolução das oito sessões recentes, histórico paginado, detalhes por atividade, eventos com busca, metadados de arquivos e exportação de sessões em CSV.

Para ler telemetria do banco, execute uma vez no SQL Editor o arquivo `Supabase/005_participant_telemetry_analysis.sql` da raiz do projeto Unity (incluído na pasta Supabase do pacote). Não precisa colar outro UUID: as consultas herdam a visibilidade dos participantes já autorizada pelas políticas RLS. O schema modular 002 e o cadastro 003 devem existir. Essa migração concede apenas leitura das sessões, eventos, catálogo de atividades e índice de arquivos; não implementa gravação de telemetria nem acesso público ao Storage.

O Unity mantem backup local e uma fila de envio ao Supabase. Depois da migracao 007 e da autorizacao do responsavel, o envio e automatico. Para analisar uma sessao local sem aguardar o envio, abra o participante correspondente e clique em **Analisar Session.json do Unity**. O arquivo esta em `Application.persistentDataPath/Sessoes_Pendentes/<session_id>/Session.json`. Essa analise manual acontece em uma thread do navegador, sem enviar o arquivo selecionado ao servidor. E necessario que `ParticipantCode` corresponda exatamente ao UUID do cadastro aberto.

As origens **Sessões do Supabase** e **Arquivos locais analisados** são separadas. Abrir de novo o mesmo UUID de sessão substitui a análise local anterior, sem somar duas vezes. Não há upload automático ou persistência dos arquivos importados. Ao voltar ou encerrar o acesso, os relatórios locais são descartados. Limites: 32 MB por arquivo, 20 sessões locais por acesso ao participante e 10.000 eventos por sessão; consultas paginadas ao banco também têm limite explícito de 10.000 linhas por recurso. Resultados parciais não são anunciados como completos.

A taxa de acerto é `acertos / tentativas`; sessões sem tentativas aparecem com `—`. Divergências entre os contadores são indicadas, sem apresentar uma taxa enganosa. `ended_utc` preenchido indica encerramento registrado; sua ausência não comprova que o simulador está rodando. As atividades usam `task_key`, com fallback para `object_id`, conforme o schema fornecido. Headset e controles mostram contagens recebidas do banco; os arquivos privados permitem examinar as amostras completas.

Se a tela indicar falta de autorização, confira as migrações 003, 005 e 007; uma falha de permissão ou rede não será apresentada como ausência de sessões. O CSV neutraliza conteúdo que poderia virar fórmula em planilhas. O download dos arquivos do Storage exige autenticação e verifica tamanho, UUIDs e SHA-256, sem expor links públicos. Registros legados sem hash continuam disponíveis como metadados.

## Configuração

`dist/config.js` contém somente a Project URL e a chave pública. A URL foi corrigida para o projeto confirmado pelo responsável; a chave do projeto antigo foi removida. Na cópia completa do projeto Unity, executar `Tools/ConfigurarSupabase.ps1` e colar a Publishable key quando solicitada atualiza frontend e Unity juntos. Em uma cópia isolada deste frontend, preencher publicKey em dist/config.js com a chave pública do mesmo projeto antes de entrar. Senha e token são digitados/recebidos durante o uso e permanecem em memória, sem localStorage, cookies de autenticação do Supabase ou arquivos de credenciais.

Antes do primeiro cadastro, executar no Supabase SQL Editor o arquivo `Supabase/003_frontend_participants.sql` da raiz do projeto Unity. Substituir `COLE_UUID_DO_RESPONSAVEL` pelo UUID da conta de **Authentication > Users** e executar o script inteiro. O script valida a existência da conta e concede leitura/cadastro somente a ela. Não libera acesso anônimo nem escrita em sessions, eventos ou arquivos de telemetria. Uma política SELECT anterior não concede INSERT por si só.

O frontend pede o mesmo e-mail/senha da conta de responsável usada no Unity. O Site novo é privado por padrão e tem, além desse login do aplicativo, o controle de acesso da hospedagem Sites.

A chave pública fica gravada; não é solicitada a cada uso. Em caso de falha de rede, usar **Verificar conexão** na tela de login. O teste consulta somente a saúde do serviço de autenticação, sem enviar e-mail ou senha. Recarregar com Ctrl+F5 após atualizar os arquivos.

## Uso

1. Entrar com a conta autorizada.
2. Digitar o nome e cadastrar. O UUID é gerado em formato N (32 caracteres, sem hífens), compatível com a PK textual do banco e o Unity.
3. Conferir “Recém-adicionados” (cadastros desta sessão de acesso) e “Participantes cadastrados” (consulta ao banco, com busca e atualização).
4. No MainMenu do Unity, usar o acesso automático configurado e clicar em Selecionar participantes. A lista consulta o Supabase a cada 5 segundos; Atualizar permite uma consulta manual. Selecionar uma pessoa e voltar ao menu para liberar Iniciar. O frontend pode ser fechado depois do cadastro confirmado.
5. A coleta começa em SampleScene e termina após a última tarefa da última quest. Depois de aplicar a migração 007, o Unity mantém o backup local e envia automaticamente os quatro arquivos e os registros da sessão. Aguarde a confirmação no Console e atualize a análise no frontend. Falhas de rede mantêm os dados na fila para nova tentativa.

Nomes iguais são permitidos e distinguem pessoas por UUID. Repetir uma requisição de cadastro após falha de confirmação usa a mesma chave para evitar duplicações. O aplicativo só mostra sucesso depois de confirmar o registro. Ao encerrar o acesso ou expirar o token, os dados da sessão são retirados da interface.

Login e leitura não concedem cadastro automaticamente. Se houver recusa ao inserir, executar 003 com o UUID da conta que fez login. O arquivo `Supabase/004_diagnosticar_permissoes_participants.sql` da raiz do projeto Unity permite conferir privilégios e políticas sem modificar o banco.

## Desenvolvimento e testes

```powershell
node --preserve-symlinks-main --preserve-symlinks serve.mjs
```

Abrir http://127.0.0.1:4173. O servidor escuta apenas no computador local e serve somente os arquivos da aplicação em dist.

```powershell
node --preserve-symlinks-main --preserve-symlinks --test tests/client.test.mjs tests/static.test.mjs tests/telemetry.test.mjs tests/analysis-ui.test.mjs tests/worker.test.mjs tests/server.test.mjs tests/storage.test.mjs tests/admin.test.mjs
```

A aplicação pronta para hospedagem está em `dist/`. `.openai/hosting.json` guarda a identidade do Site; preservar esse arquivo para atualizar o mesmo endereço.

Depois de atualizar estes arquivos, reinicie o servidor Node (Ctrl+C e execute novamente o comando de início) e recarregue com Ctrl+F5. Isso permite que o servidor carregue as novas rotas dos módulos de análise. Uma porta alternativa pode ser informada após `serve.mjs`, por exemplo `serve.mjs 4174`.

## Limites

Edição administrativa de nomes e exclusão de cadastros sem histórico são feitas por RPCs validadas, sem alterar UUIDs. Não há exclusão de sessões/arquivos nem edição de métricas brutas pelo painel. A lista é paginada, com teto explícito de 10.000 registros, e não anuncia um resultado parcial como completo. Não há renovação automática ou persistência do token: entrar novamente após expiração. O nome é limitado a 120 caracteres e exibido como texto, sem interpretação de HTML.
