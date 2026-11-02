# Comparação da integração: LogicGatesGame e TCC_VR

> Esta analise registra o estado anterior ao uploader modular. A implementacao posterior, usando somente Supabase, esta descrita em [EnvioTelemetriaSupabase.md](EnvioTelemetriaSupabase.md).

Análise estática realizada em 01/11/2026. Referência: `C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main`. Projeto atual: `C:/Users/Prokopowiski_labcg/Desktop/TCC_VR-development`.

Foram examinados os scripts de acesso ao Supabase, seleção, persistência, telemetria, fila e envio, além das referências nas cenas e no prefab de sincronização. Nenhum script, configuração de credenciais, cena ou banco foi alterado por esta análise. O projeto de referência não foi executado e suas regras remotas do Supabase/Firebase não estão disponíveis para confirmação.

## Diferença principal

O projeto de referência implementa uma integração híbrida:

```text
Supabase / tabela players → nomes no menu → nome escolhido em PlayerSession
                                          ↓
                           sessão local em JSON + poses em CSV
                                          ↓
                         CSV → Supabase Storage
                         resumo + link do CSV → Firebase Firestore
```

No projeto atual:

```text
Frontend → Supabase / participants → seleção por UUID no MainMenu
                                          ↓
                            ApplicationSessionContext
                                          ↓
                          coleta ao longo de SampleScene
                                          ↓
                   Session.json + Head.csv + Controllers.csv + Events.csv
                          salvos no dispositivo ao encerrar

Frontend de análise → lê sessões existentes no Supabase
                    → ou analisa um Session.json selecionado localmente
```

O projeto de referência já possui um consumidor de fila que tenta upload. No projeto atual, o encerramento modular grava os arquivos, mas ainda não os envia automaticamente ao banco ou ao Storage.

## Comparação

| Aspecto | LogicGatesGame | Projeto atual |
| --- | --- | --- |
| Cadastro consultado | `players`, lendo apenas `name` | `participants`, lendo `participant_code` e `display_name` |
| Identidade do participante | Apenas nome, em uma propriedade estática | UUID do cadastro, nome e sessão em contexto persistente |
| Lista | Consulta os mais recentes; limite padrão 5 no script, sobrescrito para 4 nas cenas dos menus | Consulta paginada, com limite explícito de 10.000 participantes |
| Autenticação Supabase nesses clientes | Chave JWT `anon` nos cabeçalhos; sem chamada a Supabase Auth | Publishable key identifica a aplicação; JWT de Authentication identifica o responsável |
| Persistência entre cenas | `PlayerSession.SelectedPlayerName` estático | `ApplicationSessionContext` com `DontDestroyOnLoad` e proteção de sessão ativa/pendente |
| Resumo de sessão remoto | Documento em `telemetrySessions` no Firestore | Schema SQL definido para `sessions` e `session_events`; gravação modular automática ainda ausente |
| Arquivos de pose remotos | Upload CSV para Supabase Storage | Arquivos locais; ainda sem uploader modular |
| Estado de envio | Pasta `pending`, flags de CSV enviado e arquivamento em `uploaded` | `Sessoes_Pendentes` e estados de salvamento local; não há confirmação de upload |
| Gravação das amostras | CSV incremental, buffers e escrita em `Task.Run` | Listas em memória, JSON e CSV gravados ao encerrar |
| Amostragem configurada | Intervalo 0,1 s: 10 Hz | 10 Hz no headset e nos controles em SampleScene |
| Rotação | Quaternion com quatro componentes | Euler com três componentes |
| Outras informações | Plataforma, dispositivo, circuito, portas, conexões, pontuação e campo `abandoned` | Tentativas, acertos, erros, eventos de tarefas/quests, versão da aplicação, rastreio e joysticks dos controles |
| Transporte | `UnityWebRequest` e `Task.Yield`, sem timeout/cancelamento explícitos no cliente examinado | `UnityWebRequest` com timeout e cancelamento no serviço modular de leitura |

Os assets de configuração apontam para projetos Supabase diferentes. As credenciais e os nomes das tabelas do outro projeto não devem ser copiados presumindo compatibilidade com o schema atual.

## Arquivos decisivos da referência

### Consulta e escolha por nome

[SupabasePlayersClient.cs](C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main/Assets/LogicGatesGame/Scripts/SupabasePlayersClient.cs:33) executa `GET /rest/v1/players?select=name&order=created_at.desc&limit=...`. A estrutura de parsing ignora `id` e `created_at` deliberadamente; o resultado é uma `List<string>`.

[MainMenuManager.cs](C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main/Assets/LogicGatesGame/Scripts/MainMenuManager.cs:32) salva a escolha em [PlayerSession.cs](C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main/Assets/LogicGatesGame/Scripts/PlayerSession.cs:10). O registro remoto recebe `playerName`, sem o ID da linha de `players`. Duas pessoas com o mesmo nome não ficam distinguidas por esse vínculo.

No projeto atual, [ParticipantRepository.cs](C:/Users/Prokopowiski_labcg/Desktop/TCC_VR-development/Assets/MainProjeto/Telemetria/Participants/ParticipantRepository.cs:27) conserva a chave real do cadastro. Essa abordagem deve ser mantida para relacionar sessões e resultados.

### Sincronização depende do Firebase

[TelemetryFirestoreSync.cs](C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main/Assets/LogicGatesGame/Scripts/Telemetry/TelemetryFirestoreSync.cs:58) retorna antes de iniciar a sincronização quando `_firebaseReady` está falso. Depois de inicializar o Firestore, ele percorre os registros pendentes e:

1. Envia o CSV ao Supabase Storage, se necessário.
2. Atualiza o JSON local com o estado desse upload.
3. Grava o resumo no documento cujo ID é `session.sessionId` no Firestore.
4. Arquiva o CSV e o registro local após a conclusão do fluxo.

O método manual `TestSupabaseCsvUpload` chama diretamente o upload. Assim, o teste de CSV pode funcionar mesmo que o restante da sincronização esteja bloqueado pelo Firebase. Seu sucesso também não comprova a gravação de uma sessão no PostgreSQL do Supabase.

Remover apenas o SDK Firebase quebraria a compilação dessa classe, que usa os tipos `FirebaseApp`, `FirebaseFirestore` e a extensão `ContinueWithOnMainThread`. A substituição precisa ocorrer no serviço de sincronização e na chamada feita por `TelemetryManager`.

### Configuração efetiva das cenas

As quatro cenas `MainMenuScene`, `FPMainMenuScene`, `GameScene` e `FPGameScene` referenciam o prefab `FireBaseTelemetry`. O prefab usa `SupabaseConfig` e habilita sincronização no início e na retomada.

O bucket padrão do script/prefab é `telemetry-pose-csv`, mas as cenas têm override para `sessions`. Esse override precisa ser considerado ao reproduzir o comportamento.

Há também overrides antigos de `supabaseAnonKey` e `supabaseProjectUrl` nas cenas. A classe atual lê `supabaseConfig`; esses nomes antigos não correspondem aos campos atuais da classe. Para revisar a configuração, deve-se inspecionar o asset referenciado e o campo de bucket efetivo, sem pressupor que um valor antigo visível no YAML é utilizado.

### Fila e gravação incremental

[TelemetryLocalStore.cs](C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main/Assets/LogicGatesGame/Scripts/Telemetry/TelemetryLocalStore.cs:11) separa os diretórios de pendentes e enviados. O identificador da sessão é reutilizado no documento e no caminho do CSV, e `poseCsvUploaded` evita repetir um upload já confirmado e persistido localmente.

[TelemetryPoseRecorder.cs](C:/Users/Prokopowiski_labcg/Downloads/logic-gates-game-main/logic-gates-game-main/Assets/LogicGatesGame/Scripts/Telemetry/TelemetryPoseRecorder.cs:227) escreve as amostras em lotes em outra thread; o limite configurado é 600 amostras. Isso reduz a quantidade de poses mantidas em memória e preserva os lotes já gravados antes de terminar a partida. Vale estudar essa abordagem para sessões longas do simulador atual.

## Limitações observadas na referência

- **Vínculo somente pelo nome:** faltaria acrescentar e transportar o UUID real do participante para atender ao modelo de pesquisa atual.
- **Firebase necessário para o envio automático:** o upload de CSV está dentro de um fluxo condicionado à inicialização do Firestore.
- **Sem confirmação local de gravação:** `SaveCompletedSession` captura falhas de disco e retorna `void`; mesmo assim `TelemetryManager` marca `_telemetrySaved = true` após chamá-lo. Uma falha de gravação pode ser tratada pelo chamador como sessão salva. O gerenciador atual preserva `pendingPayload` quando `TelemetryLocalStorage.Save` falha.
- **Reenvio condicionado a gatilhos:** as tentativas ocorrem no início, na retomada ou ao solicitar sincronização após salvar; não há temporizador de backoff nem observação contínua de conectividade nesses scripts.
- **Fila interrompida por erro:** a sincronização para quando encontra um registro ilegível ou quando o envio de uma sessão falha. Um arquivo problemático pode impedir o processamento dos seguintes até ser tratado.
- **URL pública presumida:** o uploader monta um endereço `/storage/v1/object/public/...`; não verifica o download após enviar. Esse endereço não demonstra que o bucket remoto é público ou que o arquivo pode ser baixado.
- **Logs de dados:** o cliente de nomes registra o corpo da resposta e a mensagem de erro pode incluir conteúdo devolvido pelo serviço. O transporte modular atual evita registrar os nomes retornados.
- **Abandono requer ligação do fluxo:** existe `SaveAbandonedSessionAsync`, chamado por `SaveTelemetryAndExitToMainMenu`. Não encontrei chamada adicional nem UnityEvent com esse último método nas quatro cenas examinadas; a presença do método não comprova que toda saída pelo menu salve uma sessão abandonada.
- **Documentação parcialmente desatualizada:** o documento `Circuitos-e-Telemetria.md` diz que sessões abandonadas não geram registro, enquanto o código já possui esse caminho de salvamento. A ligação efetiva das ações de saída precisa ser testada no Editor.

## O que falta no projeto atual

O encerramento em [TelemetrySessionManager.cs](C:/Users/Prokopowiski_labcg/Desktop/TCC_VR-development/Assets/MainProjeto/Telemetria/Telemetry/Core/TelemetrySessionManager.cs:172) chama o armazenamento local e marca a sessão como salva localmente. Não há chamada ao serviço de upload nesse caminho.

Existe uma classe legada [SupabaseManager.cs](C:/Users/Prokopowiski_labcg/Desktop/TCC_VR-development/Assets/MainProjeto/Telemetria/Supabase/SupabaseManager.cs:18) que tenta chamar a RPC `salvar_telemetria_completa`. A busca nos scripts e callbacks serializados não encontrou chamadas a `SendTelemetryData` fora da própria declaração. Tampouco encontrei a definição SQL dessa RPC nos scripts atuais do workspace. Portanto sua existência não comprova que o fluxo modular já envie dados.

Essa classe legada ainda usa a chave pública no cabeçalho `Authorization`. Com a Publishable key configurada atualmente, o cabeçalho de autenticação de usuário deve ser fornecido por um JWT de Authentication, como já faz o serviço modular de leitura. Não basta chamar a classe legada sem revisar autenticação, parâmetros da RPC e permissões de escrita.

[005_participant_telemetry_analysis.sql](C:/Users/Prokopowiski_labcg/Desktop/TCC_VR-development/Supabase/005_participant_telemetry_analysis.sql:23) permite leitura para a análise do frontend. Ele não autoriza inserir sessões, eventos ou objetos no Storage. Essas permissões precisam ser definidas para o futuro uploader.

## Adaptação recomendada para usar somente Supabase

Preservar o cadastro, a seleção por UUID, o contexto de sessão e a coleta atuais. Acrescentar o equivalente à fila e ao consumidor de envio da referência, com destino integralmente Supabase:

1. **Fila durável:** reconhecer os arquivos finalizados em `Sessoes_Pendentes/<session_id>` e manter um manifesto de envio separado do resultado da coleta.
2. **Autenticação disponível entre cenas:** fornecer ao uploader um JWT do responsável, renovável e independente da interface do menu. O estado de autenticação atual pertence ao fluxo do menu e precisa ter um ciclo de vida adequado ao envio em outras cenas. O armazenamento de renovação implementado atualmente é específico do Windows.
3. **Storage privado:** enviar `Session.json`, `Head.csv`, `Controllers.csv` e `Events.csv` para caminhos determinísticos por UUID de sessão. Os nomes e o bucket devem corresponder ao modelo atual, e não aos da referência.
4. **Gravação SQL:** criar uma API/RPC autenticada para persistir o resumo em `sessions`, os eventos em `session_events` e os índices de arquivos em `session_files`, respeitando o cadastro do participante e as políticas de escrita.
5. **Reenvio sem duplicação:** conservar `session_id`; fazer a operação SQL de cada lote de forma transacional e definir deduplicação para os eventos. A PK de `sessions` sozinha não impede duplicar linhas de `session_events` em um retry. O upload de Storage e a transação SQL precisam de confirmações separadas.
6. **Confirmação e retomada:** marcar envio concluído apenas após confirmar arquivos e SQL; repetir falhas transitórias com espera progressiva, timeout e cancelamento. O próximo participante pode usar o menu enquanto arquivos de sessões anteriores continuam na fila.

Além do envio, podem ser úteis campos opcionais como plataforma, modelo do dispositivo e motivo de encerramento. Eles devem ser acrescentados com evolução do schema e sem importar a fórmula de pontuação de um jogo de portas lógicas como se já fosse adequada às atividades de circuitos elétricos deste simulador.

Os formatos de telemetria também são diferentes: o JSON da referência usa `sessionId`/`playerName` e seu CSV usa quaternion. O frontend atual espera o payload `SessionId`/`ParticipantCode` com eventos e listas de amostras. Uma conversão explícita seria necessária para importar arquivos do outro jogo; apenas copiar o arquivo não cria compatibilidade.

## Base documental das recomendações de acesso

A API key identifica a aplicação e Supabase Auth identifica quem faz a chamada. Operações de upload têm políticas próprias em `storage.objects`; permissões nas tabelas SQL não substituem essas políticas. Para arquivos privados, usar downloads autenticados ou URLs assinadas, em vez de presumir uma URL pública funcional. Referências oficiais: [API keys](https://supabase.com/docs/guides/getting-started/api-keys), [controle de acesso ao Storage](https://supabase.com/docs/guides/storage/security/access-control), [download de arquivos](https://supabase.com/docs/guides/storage/serving/downloads).

Esta comparação confirma as diferenças do código e da configuração local examinados. A existência das tabelas, funções, buckets e políticas nos serviços remotos, assim como o sucesso real de upload, ainda exige validação com acesso aos respectivos projetos.
