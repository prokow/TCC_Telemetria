# Telemetria: gravação incremental e memória

## Comportamento implementado

A coleta de cabeça e controles continua em 10 Hz, com o mesmo contrato JSON (SchemaVersion 2) e os mesmos quatro arquivos finais: Session.json, Head.csv, Controllers.csv e Events.csv. Nenhuma cena precisa ser reconfigurada para habilitar as alterações.

- O TelemetrySessionManager inicia um checkpoint e depois grava novos blocos a cada **15 segundos**. O checkpoint também é antecipado quando os buffers atingem metade da capacidade. O intervalo aparece no Inspector como `Checkpoint Interval Seconds` (1–30 segundos).
- Cada checkpoint transfere a posse das listas para um worker. A coleta continua em listas novas enquanto o bloco anterior é gravado. Há somente um checkpoint em processamento por sessão; uma fila de blocos não cresce indefinidamente.
- Os blocos são gravados em `Application.persistentDataPath/Sessoes_Pendentes/<UUID>/Chunks/00000000.json`, etc. A escrita usa arquivo temporário, flush e renomeação; arquivos `.tmp` incompletos não entram na recuperação.
- Ao encerrar, os blocos são unidos em JSON e CSV por streaming, fora da thread principal. Os históricos deixam de ser referenciados após a confirmação da escrita. Os checkpoints só são removidos depois de confirmar os arquivos finais e `Ready.json`.
- `EndSession()` e `RetryPendingSave()` agora indicam **solicitação aceita**, não conclusão. Código que precisa saber se já salvou usa `await EndSessionAsync()` / `await RetryPendingSaveAsync()`. Os chamadores existentes foram atualizados. `IsSaving`, `HasPendingSave`, `LastStatus` e `StatusChanged` expõem o andamento.
- O uploader espera a finalização, valida o JSON amostra por amostra, calcula hashes lendo os arquivos e usa `UploadHandlerFile`. A conferência de duplicatas usa `DownloadHandlerFile` e hash do arquivo baixado, sem guardar o download inteiro em RAM. Os CSVs existentes são reutilizados se seus hashes e o hash do JSON corresponderem ao cache local.
- O limite de JSON foi alinhado ao bucket já definido na migração 007: **64 MiB por arquivo**. O tamanho é verificado antes do parsing no preparo do pacote. O importador web também foi ajustado para esse limite. Não é necessária nova migração de banco nem alteração do formato dos dados.
- Escritas do estado de upload também usam worker. No Windows, a substituição atômica tenta novamente por até 300 ms quando há bloqueio transitório do arquivo; a versão válida anterior não é apagada antes da substituição.

## Limites e falhas

Cada gravador mantém até 600 amostras no buffer atual. A lista corrente de eventos é limitada a 128 eventos regulares, mais um evento reservado para explicar uma parada de proteção. Existe ainda o bloco em processamento ou aguardando nova tentativa. A 10 Hz, normalmente os blocos contêm cerca de 150 amostras por gravador, não a sessão inteira.

Se o disco ficar indisponível ou o worker não acompanhar a coleta, os buffers não crescem sem limite: **a telemetria é encerrada, mas a atividade continua**. O motivo é registrado como `collection_stopped` quando possível e permanece no status/log. A interface `TelemetrySessionInput`, quando presente, acompanha o status. A coleta também encerra ao alcançar aproximadamente 56 MiB em checkpoints ou o limite de eventos, deixando margem antes do teto de 64 MiB. Isso não divide uma experiência em várias sessões automaticamente.

Campos textuais dos eventos respeitam os limites existentes da ingestão: tipo até 120 caracteres, objeto até 200 e detalhes até 4.000; excedentes são truncados. As tentativas posteriores ao encerramento da telemetria não entram nessa sessão.

Uma falha de salvamento mantém o bloco pendente em memória e os checkpoints confirmados no disco. A nova tentativa não duplica as amostras nem altera o JSON já finalizado. Ao abrir o app, sessões interrompidas são reconstruídas até o último checkpoint confirmado e recebem um evento `session_recovered`. Um gravador ainda ativo é ignorado pela recuperação; uma sequência com lacunas é preservada para inspeção.

**Um encerramento abrupto ainda pode perder a cauda não confirmada em disco.** Em condições normais isso corresponde aproximadamente ao intervalo de checkpoint, mais o tempo da escrita. Pause solicita um checkpoint, mas a suspensão/encerramento do processo pode ocorrer antes de o worker terminar. Não há garantia de perda zero nem de ausência de crash sem medição no dispositivo real.

Os arquivos finais continuam preservados após upload. Não foi adicionada exclusão automática de sessões confirmadas; é necessário acompanhar o espaço livre do dispositivo.

## Validação executada

- Suíte C#: **368 verificações aprovadas**, compilando os scripts reais com as bibliotecas do Unity 6000.3.14f1. Inclui checkpoint parcial, recuperação, lacuna de sequência, tentativa após falha de escrita, CSV compatível, integridade do cache, envio parcial, cancelamento, conflito e retomada.
- Teste de pacote com JSON válido acima de 32 MiB: aceito, com retenção adicional de memória gerenciada abaixo de 4 MiB no teste isolado. O cenário usa whitespace para atingir o tamanho; não representa uma medição de poses reais nem do pico total de RAM. Arquivo acima de 64 MiB rejeitado.
- Frontend: **33 testes aprovados**, incluindo interoperabilidade dos quatro arquivos reais gerados pelo C# e rejeição acima do novo limite.
- Associação entre quests e coleta: **13 verificações aprovadas**.

Comando local usado para a suíte C#:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ParticipantIntegrationChecks/Run.ps1 -UnityEditor 'D:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor'
```

Essas verificações não executam uma sessão em VR, uma build Android/IL2CPP ou uma transferência real ao Supabase. A autenticação e a rede são simuladas nos testes.

## Antes do teste no headset

O fluxo de login existente ainda instancia `WindowsSupabaseSessionStore`, que rejeita plataformas diferentes de Windows. **Essa dependência de autenticação não foi alterada nesta implementação de memória/persistência** e precisa de uma solução própria para Android antes de testar o fluxo completo no headset. A coleta local e a recuperação não dependem desse armazenamento de credenciais.

Validar uma Development Build no hardware final, observando o Profiler durante coleta, encerramento e upload. Testar a maior duração esperada, várias sessões consecutivas, suspensão/retomada, perda de rede e recuperação após encerrar o processo. Confirmar que o contador de amostras final corresponde à soma dos blocos e que o pico de memória deixa margem para o cenário e o runtime XR.

Referências de API: [UploadHandlerFile](https://docs.unity3d.com/ja/current/ScriptReference/Networking.UploadHandlerFile.html) e [DownloadHandlerFile](https://docs.unity3d.com/cn/6000.0/ScriptReference/Networking.DownloadHandlerFile.html).