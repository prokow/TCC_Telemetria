# Narrativas, botao de proxima quest e CSVs de movimentos

## Onde estao os movimentos

O botao **Exportar sessoes - CSV** do frontend gera `sessoes-<participante>-supabase.csv`, com resumo por sessao: duracao, tentativas, acertos, erros, datas e UUIDs. Esse CSV nao contem as amostras de movimento.

Para baixar os movimentos: **Ver analise > Sessoes do participante > Detalhes > Arquivos de telemetria registrados**. Escolha **Headset - CSV** (`Head.csv`) ou **Controles - CSV** (`Controllers.csv`) e clique em **Baixar arquivo**. O download e autenticado e confere UUIDs, tamanho e SHA-256.

| Arquivo | Conteudo |
| --- | --- |
| Head.csv | ParticipantCode, SessionId, TimeSinceStart, posicao XYZ e rotacao Euler XYZ da Main Camera/HMD |
| Controllers.csv | UUIDs/tempo, rastreio esquerdo/direito, posicao/rotacao de cada controle e analogicos |
| Events.csv | Eventos e marcadores de quest/tarefa, incluindo tempo desde o inicio |
| Session.json | Payload completo com resumo, eventos e amostras |

As amostras usam o relogio da sessao e coordenadas mundiais. Os arquivos abrangem a experiencia inteira; os eventos `quest_started`/`quest_completed` permitem relacionar os intervalos a cada quest. Pose da cabeca nao equivale a rastreamento ocular.

Na SampleScene, os dois recorders estao habilitados a **10 Hz**. As referencias foram conferidas no prefab XR: **Main Camera**, **Left Hand** e **Right Hand**. A coleta automatica esta ligada ao modelo de quests.

Foi inspecionada, sem modificar dados, uma sessao real local `c7ca6e7d363646cf82499a7a73ef8b66`: **6.399 amostras do headset** e **6.399 dos controles**, com variacoes nos valores de pose/entrada. O manifesto local registra `Uploaded` e quatro arquivos confirmados. Isso verifica os arquivos e o registro local de ACK; nao foi feita uma consulta administrativa independente ao Supabase remoto.

A pasta local do projeto neste computador e `%USERPROFILE%/AppData/LocalLow/DefaultCompany/SaborTCC/Sessoes_Pendentes/<session_id>/`. Tambem ha uma sessao antiga em `ReviewRequired`, cujo UUID de participante nao estava cadastrado/autorizado; ela nao deve ser confundida com um envio confirmado. Para uma coleta real, entre pelo MainMenu e selecione um participante cadastrado.

## Problemas corrigidos na progressao

1. O clique da ultima tarefa chamava CompleteTask, que disparava `onQuestComplete -> TurnOnButton` durante o proprio clique. Isso podia deixar o botao novamente habilitado para a proxima quest. O estado agora impede esse rearme e e limpo em `onQuestLoaded`.
2. TurnOnButton aceitava qualquer pedido. Agora valida a quest atual e as tarefas: um pedido com tarefa so e aceito quando ela e a ultima pendente e suas dependencias foram concluidas; um pedido sem tarefa exige todas concluidas.
3. TriggerNextQuestTransition permitia avancar sem conferir as tarefas. O modelo agora exige conclusao completa, alem dos bloqueios de transicao em andamento e fim da experiencia.
4. A narrativa com Auto Concluir ativado podia iniciar ignorando suas dependencias. Agora todas as narrativas precisam ter sua tarefa disponivel.
5. Narrativas finais com Auto Concluir desmarcado aguardam todas as demais tarefas. O botao so recebe a tarefa apos terminar a lista de falas e seus intervalos.
6. O tempo de espera do audio considera o pitch. Narrativas antigas sao canceladas ao desativar o circuito ou trocar a quest. A narrativa pode se preparar durante o fade, mas aguarda o fim da transicao antes de tocar, sem perder o evento de inicio.

As assinaturas dos UnityEvents e os nomes dos campos serializados foram preservados. Nao foram regravados prefabs, cenas ou arquivos historicos de telemetria. Os scripts ajustados sao `QuestSystemModel`, `NextQuestButton` e `SequentialRobotNarrative`, apoiados pelas regras/estado em `QuestAdvanceRules.cs`.

## Conferencia da configuracao real da SampleScene

Foram verificadas as **12 narrativas das sete quests**, incluindo os overrides da cena (que podem ser diferentes do prefab original). As tarefas de narrativa pertencem as respectivas quests, os audios referenciados existem e as narrativas que entregam tarefas ao botao apontam para a ultima tarefa da lista e possuem o botao atribuido.

| Fase | Narrativa inicial / final |
| --- | --- |
| Falas iniciais | A fala entrega Task_Robo_1 ao botao; e a unica tarefa da fase |
| Tutorial | Inicial conclui Tutorial 0; final entrega Tutorial 4 ao botao |
| LED 1 | Inicial conclui FaseLED1_0; final entrega FaseLED1_5 |
| LED 2 | Inicial conclui FaseLED2_0; final entrega FaseLED2_Final |
| Divisor 1 | Inicial conclui FaseDV_1 0; final entrega FaseDV_1 4 |
| Divisor 2 | Inicial conclui FaseDV_2 0; final entrega FaseDV_2 3 |
| Falas finais | Auto Concluir conclui Task_Robo_2 e encerra a experiencia/coleta; nao ha proxima quest |

Para as falas finais das fases intermediarias, mantenha **Auto Concluir desmarcado**, atribua a ultima task em **Fala Robo Task** e o botao global em **Botao Proxima Quest**. Para as falas iniciais dessas fases, Auto Concluir deve estar marcado e a tarefa deve ser a de introducao. A primeira fase, que possui apenas uma tarefa de fala/avanco, mantem o comportamento de entregar essa tarefa ao botao. A ultima fase permanece com conclusao automatica depois das falas.

## Testes e limites

Execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\SampleSceneChecks\Run.ps1
```

O roteiro verifica referencias da cena, os grafos reais das 32 tarefas, as narrativas com overrides, compila os scripts com bibliotecas Unity/XRI e testa as regras de avanco para todas as combinacoes de conclusao dos grafos. Cobre pedido antecipado, task estrangeira/bloqueada, ultimo clique, callback reentrante, troca de fase, repeticao e fim da experiencia. Os testes de telemetria conferem valores numericos de posicao, rotacao e analogico nos CSVs gerados. O Worker web tambem le os arquivos gerados pelo C#.

Relatorios em `Temp/SampleSceneChecks`: `scene-assets-report.json`, `narratives-report.json` e `movement-csv-report.json`. Os CSVs reais foram apenas lidos, sem expor nomes de participantes no relatorio.

Resultados locais: 8.715 verificacoes de referencias/grafos, 131 de narrativas, 12.267 de regras/estado/limites/ocupacao, 340 de integracao C# e 67 testes web aprovados. Os 13 checks existentes da ligacao quests/coleta tambem passaram. Esses numeros incluem analise estatica e testes fora do motor, sem substituir a execucao fisica no headset.

**Tools > Circuitos VR > Testar SampleScene sem Play Mode** inclui um teste adicional do botao com callbacks reais do XR em uma cena temporaria, alem dos modelos de quests reais. Esse comando precisa ser executado no Editor pelo responsavel; nao houve confirmacao de execucao dele neste ambiente.

Nao foi executada uma experiencia fisica completa no Oculus, nem reproduzidos todos os audios em Play Mode. O teste em batch anterior tinha falha de licenciamento e nao foi repetido sem evidencia de recuperacao. Para validar no headset: pressione durante tarefas incompletas (deve ficar bloqueado), termine a narrativa final, pressione uma vez, e confirme que a quest seguinte comeca com o botao bloqueado. Na ultima fase, a coleta deve terminar uma unica vez depois das falas finais.
