# Uma coleta entre duas quests

Na `SampleScene`, selecione `TelemetrySystem` e abra o componente
`AutoTelemetrySession`. Preencha a lista **Telemetry Quest Sequence** com os
mesmos assets de quest usados na sequência do `QuestSystemModel`:

- Primeiro elemento: quest cujo carregamento inicia a telemetria.
- Último elemento: quest cujo carregamento encerra e salva a telemetria.
- Elementos intermediários são opcionais e não criam sessões separadas.

É suficiente usar Size = 2 e arrastar os assets de início e encerramento.
Configure-os na ordem em que acontecem no jogo. A lista não altera a sequência
de quests do jogo e não é um filtro: todas as interações entre os dois limites
entram na mesma sessão, inclusive em quests não listadas.

Exemplo: `[Quest A, Quest D]` coleta A, B e C se essa for a ordem do jogo.
Ao carregar D, a coleta para antes de registrar `quest_started` de D. O evento
`telemetry_interval_completed` identifica a quest que provocou o encerramento.
O carregamento ocorre durante a transição de tela; não espera o fim do fade ou
da narrativa.

A lista começa vazia para que os limites sejam escolhidos no Inspector.
Sem pelo menos duas referências válidas, ou com a mesma quest nos dois extremos,
a coleta automática não inicia e o Console explica o motivo. Configure antes
de entrar em Play Mode; os limites são fixados ao inicializar o componente.

Uma sessão produz um único conjunto de `Session.json`, `Head.csv`,
`Controllers.csv` e `Events.csv`, com os totais de todo o intervalo.
Concluir uma quest intermediária não interrompe a gravação. A conclusão da
experiência continua encerrando a coleta caso a quest limite não seja alcançada.
Depois de encerrada, a coleta não reinicia nessa execução do componente.
As proteções de armazenamento e o salvamento ao sair da cena continuam no
`TelemetrySessionManager`.

Validação automatizada do intervalo (dependências Unity simuladas):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TelemetryQuestIntervalChecks/Run.ps1
```

Validar também no Play Mode com os assets escolhidos: antes do início, durante
as quests intermediárias e ao entrar na quest limite. O teste isolado não
substitui a verificação de movimentos e arquivos no Unity.
