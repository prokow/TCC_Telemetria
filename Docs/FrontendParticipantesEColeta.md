# Cadastro web, seleção VR e coleta até a última quest

## Entrega

O frontend completo está em [Web/participantes-vr](../Web/participantes-vr). Ele usa a mesma Project URL, chave pública e tabela `public.participants` já configuradas no Unity. Não há banco de nomes separado no navegador. A atualização automática do menu e o diagnóstico de permissão de cadastro estão descritos em [PermissoesCadastroEUnityIndependente.md](PermissoesCadastroEUnityIndependente.md).

A área é para o **responsável autenticado**: a mesma conta de Authentication que já usa no menu VR. Inclui cadastro por nome, lista de recém-adicionados nesta sessão de acesso, lista completa de participantes cadastrados, busca, atualização, estados de erro e encerramento do acesso. Nomes iguais são permitidos; a identidade é o GUID N de 32 caracteres, compatível com o Unity.

O cadastro é confirmado pelo banco antes de aparecer como sucesso. Um retry após timeout conserva o UUID, e o INSERT usa a PK para evitar duplicação. As listas são paginadas; falhas não são apresentadas como uma lista completa vazia. Senhas, tokens e nomes da sessão não são guardados em localStorage ou arquivos. O app mostra nomes como texto, sem interpretar HTML.

## 1. Autorizar o cadastro no Supabase

O SQL anterior autorizava apenas leitura. Abra [Supabase/003_frontend_participants.sql](../Supabase/003_frontend_participants.sql), substitua **COLE_UUID_DO_RESPONSAVEL** pelo UUID da conta de **Authentication > Users** e execute o arquivo inteiro no SQL Editor do mesmo projeto.

O script verifica que a conta existe e concede SELECT/INSERT apenas nas colunas participant_code e display_name. As políticas limitam leitura e inserção ao responsável indicado. Não libera acesso anônimo, edição, exclusão ou escrita na telemetria. A PK continua sendo participant_code, não display_name.

Essa migração foi preparada localmente; não foi executada no banco remoto pelo agente. Até executá-la, o login e a leitura podem funcionar, mas o cadastro será recusado por falta de permissão de INSERT.

## 2. Abrir o frontend

Na raiz do projeto, em um terminal:

```powershell
cd Web/participantes-vr
node --preserve-symlinks-main --preserve-symlinks serve.mjs
```

Abrir **http://127.0.0.1:4173** no navegador. Esse servidor serve os arquivos de dist apenas no próprio computador. Para encerrar, use Ctrl+C no terminal.

Entre com o e-mail e a senha do responsável, digite o nome e confirme o cadastro. “Recém-adicionados” mostra os cadastros confirmados nesta sessão de acesso; “Participantes cadastrados” consulta todos os registros visíveis no banco. Encerrar acesso limpa os dados locais da interface, sem apagar o banco.

A publicação automática pelo Sites foi tentada, mas o repositório de origem não pôde ser alcançado por HTTPS/porta 443 neste ambiente. O Site foi registrado como privado e sua identidade foi preservada em `.openai/hosting.json`, porém não existe publicação confirmada nesta entrega. Não tratar o endereço esperado de registro como um site publicado. O teste HTTP/navegador também foi bloqueado pela revisão automática de permissões, que não permite aprovação de sandbox.

## 3. Selecionar no Unity

Abra ou recarregue **Assets/Scenes/MainMenu.unity**, entre em Play Mode e faça login como responsável. Abra a seleção de participantes e toque em **Atualizar** após um cadastro novo na web. O menu consulta exclusivamente os registros retornados de public.participants; não inclui a conta de Authentication como participante automaticamente.

Selecionar um registro válido prepara a sessão local e habilita Iniciar. Sem login, com consulta inválida ou sem seleção, o botão permanece bloqueado. As referências da cena, o Canvas World Space, os raycasters e os controles XR existentes são reaproveitados.

## 4. Coletar até terminar a experiência

Ao carregar **SampleScene**, AutoTelemetrySession inicia os gravadores após a inicialização dos componentes e adota o participante e UUID de sessão preparados no menu.

A sequência real da cena tem sete quests; a última é `06_FaseFalasFinais`. O robô final conclui a tarefa depois de terminar todas as falas. Agora esse término emite um evento único de sequência concluída, que registra experience_completed e encerra/salva a telemetria. Não é necessário clicar em um botão extra depois da última quest.

Alterações relevantes:

- [QuestCompletionProgress.cs](../Assets/MainProjeto/Core/Scripts/Model/QuestCompletionProgress.cs): conclusão única por quest e experiência; não trata lista vazia ou tarefas pendentes como conclusão.
- [QuestSystemModel.cs](../Assets/MainProjeto/Core/Scripts/Model/QuestSystemModel.cs): emite onQuestSequenceComplete ao concluir a última quest e impede transições concorrentes. Passa a pertencer à SampleScene, para uma nova experiência não herdar índice/circuitos de uma cena anterior destruída.
- [AutoTelemetrySession.cs](../Assets/MainProjeto/Telemetria/Telemetry/Core/AutoTelemetrySession.cs): assina os eventos de quest, registra início/fim de fases e tarefas e salva no fim da experiência. Remove os listeners ao desativar.
- [NextQuestButton.cs](../Assets/MainProjeto/Core/Scripts/NextQuest/NextQuestButton.cs): mantém a versão com Task e acrescenta um método realmente sem parâmetros, compatível com o UnityEvent já serializado na cena.
- [TelemetrySessionManager.cs](../Assets/MainProjeto/Telemetria/Telemetry/Core/TelemetrySessionManager.cs): uma suspensão temporária do app não encerra a sessão por padrão. Os eventos de pausa/retorno são registrados; existe opção explícita finishOnApplicationPause para o comportamento anterior.
- [SampleScene.unity](../Assets/Scenes/SampleScene.unity): apenas acrescentada a referência ao QuestSystemModel existente em AutoTelemetrySession.

## Onde os dados são gravados

**Os nomes cadastrados ficam no Supabase. A telemetria continua sendo salva localmente**, em:

```text
Application.persistentDataPath/Sessoes_Pendentes/<session_id>/
  Session.json
  Head.csv
  Controllers.csv
  Events.csv
```

O JSON e os CSVs usam o mesmo participante e UUID de sessão que o menu preparou. Esta entrega não implementa INSERT em public.sessions nem upload para Storage; essas operações precisam de permissões próprias e de um uploader. O sucesso do cadastro web não é uma confirmação de envio de telemetria.

## Verificação

- 212 verificações C# passaram, com compilação isolada contra as bibliotecas locais do Unity. Incluem cenário de sete quests, continuação nas fases intermediárias, não encerramento durante uma tarefa pendente, conclusão única ao terminar a última e nova experiência com estado limpo.
- 14 testes do frontend passaram, cobrindo autenticação, paginação, retry com o mesmo UUID, confirmação do cadastro, homônimos, nomes nulos existentes, erros, expiração, cancelamento e integridade dos controles/arquivos HTML.
- Validação de YAML/referências de MainMenu e SampleScene e da associação entre quests e coleta.

O login real, a execução da migração e o teste completo no headset ainda precisam ocorrer com a sua conta e o seu banco. O navegador de teste não pôde ser executado devido à política do ambiente; não houve validação visual automatizada.

Roteiro manual: cadastrar duas pessoas (inclusive homônimos), atualizar no Unity, selecionar uma, verificar Iniciar bloqueado antes da seleção, executar as sete quests e conferir que o Session.json final contém a identidade selecionada e o evento experience_completed. Uma suspensão temporária no headset deve conservar o mesmo UUID e continuar a gravação ao retornar.
