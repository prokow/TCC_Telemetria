> Esta auditoria descreve a implementação anterior. As alterações posteriores e os limites atuais estão em [TelemetriaGravacaoIncremental.md](TelemetriaGravacaoIncremental.md).

# Auditoria de memória da telemetria

Data: 2026-11-03. Escopo: `Assets/MainProjeto/Telemetria`, configuração da cena `Assets/Scenes/SampleScene.unity` e gravação/envio de sessões.

## Conclusão

O histórico de poses cresce lentamente a 10 Hz. Os maiores riscos encontrados estão na conversão integral para JSON/CSV ao finalizar, no processamento do pacote de upload e na ausência de um limite de memória/duração durante a coleta. Não é possível garantir ausência de crash por análise de código ou pelo teste isolado realizado: falta medir o Player no equipamento utilizado, junto com o restante da aplicação.

## Frequência e conteúdo

- Cabeça: 10 Hz configurados, um registro a cada 100 ms como alvo, contendo tempo, posição XYZ e rotação XYZ. Não é rastreamento ocular.
- Controles: 10 Hz configurados, um registro conjunto para as duas mãos, contendo 17 floats e dois bools. Não são dois registros independentes por amostra.
- Tentativas, acertos, erros, fases e tarefas: eventos, sem frequência fixa. Os contadores têm tamanho fixo; a lista de eventos e suas strings crescem conforme as interações.
- Sincronização: verificação aproximadamente a cada 10 segundos, com exclusão de execução simultânea e retentativas. Não há upload de cada amostra individualmente.

Os gravadores são acionados por Update e agendam a próxima amostra com `now + 1 / rate`. Logo, 10 Hz é um alvo/teto nominal; quantização por frames e quedas de FPS reduzem a frequência efetiva, sem recuperar amostras atrasadas. Por exemplo, com frames perfeitamente regulares a 72 FPS, o intervalo tende a oito frames (aproximadamente 9 Hz). O timestamp permite calcular a frequência observada de um arquivo real.

Referências: `Telemetry/Recorders/TelemetryHeadRecorder.cs:11`, `Telemetry/Recorders/TelemetryControllerRecorder.cs:18`, `Telemetry/Core/TelemetrySessionManager.cs:197`, `Telemetry/Upload/TelemetrySyncController.cs:67`. Os valores estão também serializados em `Assets/Scenes/SampleScene.unity:15106` e `:15125`.

## Teste sintético local

Executado em .NET 9.0.305 no Windows, usando as classes reais de dados, `TelemetryCsvEncoder.Encode` e `TelemetryUploadPackage.Build`. Foram criados exatamente 600 registros por minuto em cada lista, sem eventos, com floats sintéticos variáveis. Não foi executada uma sessão em VR.

Memória retida foi medida com `GC.GetTotalMemory(true)` antes/depois da criação do payload. Inclui objetos e capacidade das listas. Não representa RAM total do processo, memória reservada do heap nem pico durante serialização. O JSON foi produzido por Newtonsoft como aproximação de tamanho; a aplicação usa JsonUtility, que pode formatar os números de forma diferente. CSV usa o encoder real, executado sob .NET, que também pode diferir na formatação em relação ao runtime do Unity.

| Duração | Amostras por gravador | Histórico retido (MiB) | JSON aproximado (MiB) | CSVs (MiB) | Total dos quatro arquivos (MiB) |
| --- | ---: | ---: | ---: | ---: | ---: |
| 10 min | 6.000 | 0,90 | 3,51 | 2,18 | 5,69 |
| 30 min | 18.000 | 2,83 | 10,56 | 6,56 | 17,12 |
| 60 min | 36.000 | 5,67 | 21,15 | 13,13 | 34,28 |
| 90 min | 54.000 | 8,00 | 31,73 | 19,71 | 51,44 |
| 120 min | 72.000 | 11,35 | 42,32 | 26,29 | 68,61 (upload rejeitado) |

MiB = 1.048.576 bytes. A retenção do histórico ficou perto de 0,09–0,10 MiB/minuto no teste. Eventos e outras partes da aplicação ficam fora desse número. A capacidade das listas cresce em degraus.

| Duração | Alocação acumulada ao gerar CSVs (MiB) | Alocação acumulada ao montar upload (MiB) |
| --- | ---: | ---: |
| 10 min | 19,27 | 45,83 |
| 30 min | 58,05 | 137,43 |
| 60 min | 116,38 | 275,34 |
| 90 min | 174,70 | 412,25 |

**Alocação acumulada não é pico nem memória simultaneamente ocupada.** Inclui temporários que podem ser coletados durante a operação. O teste não mede a memória nativa do UnityWebRequest nem a gravação com JsonUtility. Os números demonstram trabalho de alocação substancialmente maior na exportação do que na retenção das poses.

A conversão dos CSVs levou aproximadamente 34/196/326/1.238 ms para 10/30/60/90 minutos nesta execução isolada. Esses tempos incluem condições locais, GC e efeitos de runtime/JIT; não são previsão de tempo no Unity ou headset. O custo por amostra, FPS e pico real de RAM permanecem sem medição no Player.

O programa reproduzível fica em `Temp/TelemetryMemoryAudit/Program.cs`, com projeto `Audit.csproj`. Comando: `dotnet run --project Temp/TelemetryMemoryAudit/Audit.csproj --configuration Release --verbosity quiet -- Temp/TelemetryMemoryAudit/Data`. A pasta Temp não é um artefato permanente versionado.

## Achados por prioridade

1. **Salvamento síncrono na thread principal.** `TelemetrySessionManager.EndSession` chama `RetryPendingSave`, que chama `TelemetryLocalStorage.Save`. O JSON completo, os três CSVs e a escrita em disco ocorrem nessa chamada. Há risco de congelamento perceptível ao concluir, mesmo sem falta de RAM. Referências: `Core/TelemetrySessionManager.cs:145`, `:182`; `Persistence/TelemetryLocalStorage.cs:29` e `:34`.

2. **Coleta sem teto e sem gravação incremental.** As listas crescem durante toda a sessão. A cena usa `finishOnApplicationPause: 0`; não há checkpoint periódico. Quit normal tenta finalizar, mas encerramento abrupto pode perder a sessão ainda apenas em RAM. Isso é risco de perda de dados, não evidência de que uma pausa cause crash. Referências: `Recorders/TelemetryHeadRecorder.cs:48`, `Recorders/TelemetryControllerRecorder.cs:85`, `Core/AutoTelemetrySession.cs:113`.

3. **Histórico permanece após salvar.** `End()` apenas desativa a coleta e o salvamento bem-sucedido limpa somente `pendingPayload`. As listas originais mantêm os objetos até novo Begin ou destruição do componente. Não se trata necessariamente de vazamento crescente entre sessões: Begin limpa os elementos. As cópias de histórico copiam referências, não duplicam todos os objetos; acrescentam arrays de referências. `List.Clear()` mantém a capacidade, conforme a [documentação .NET](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.clear?view=net-9.0). Referências: `Core/TelemetrySessionManager.cs:170` e `:186`; `Recorders/TelemetryControllerRecorder.cs:29`, `:45` e `:52`.

4. **Upload reconstrói tudo em RAM.** Build lê o JSON inteiro, cria string UTF-16, desserializa os frames, gera os CSVs novamente e retém os quatro arquivos como byte arrays. Roda em Task.Run, reduzindo bloqueio direto da thread principal, mas usa a mesma memória do processo e gera pressão de GC. UploadHandlerRaw(byte[]) também utiliza cópia em buffer nativo, conforme a [documentação Unity](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/networking/uploadhandlerraw). A fila envia sequencialmente, o que limita concorrência, mas uma nova sessão pode estar coletando durante o envio. Referências: `Upload/TelemetryUploadPackage.cs:43` e `:67`, `Upload/TelemetryUploadProcessor.cs:70`, `Upload/UnityTelemetryUploadTransport.cs:24`.

5. **Limites de envio não são limites de coleta.** O JSON é rejeitado acima de 32 MiB, e eventos acima de 10.000 também são rejeitados. A checagem de tamanho ocorre DEPOIS de ReadAllBytes, portanto não previne a alocação do arquivo grande. Uma sessão de 90 minutos sintética já se aproximou do limite; o tempo real para atingi-lo depende de números, eventos e frequência efetiva. No teste adicional de 120 minutos, Build rejeitou o JSON de 42,32 MiB com InvalidData. Se essa falha ocorrer antes da criação do manifesto de upload, state permanece null e o catch não persiste ReviewRequired; a fila pode voltar a ler o arquivo nas verificações seguintes. Um total de arquivos maior que 32 MiB não implica rejeição: o limite citado vale para Session.json. Referências: `Upload/TelemetryUploadPackage.cs:43–56`.

6. **Disco acumula sessões confirmadas.** O processador marca Uploaded e ignora uploads futuros, mas não remove esses arquivos. Isso preserva cópias locais, porém não há política de retenção/quota nessa rotina. Em grande volume pode faltar espaço e prejudicar novos salvamentos. A enumeração da fila também percorre diretórios de sessões antigas. Referências: `Upload/TelemetryUploadProcessor.cs:57` e `:76`; `Upload/TelemetryUploadStore.cs:34`.

## Mudanças recomendadas

- Prioridade imediata: liberar as referências dos históricos após confirmação do salvamento local, preservando dados em caso de falha; separar conversão e escrita da thread de renderização sem acessar objetos Unity indevidamente no worker.
- Para sessões prolongadas: persistir em blocos com buffer limitado e recuperação após interrupção. Isso exige adaptar exportação, schema e ingestão, mantendo compatibilidade; não basta alterar isoladamente o arquivo final.
- Evitar recriar CSVs e carregar todos os arquivos simultaneamente no envio. Validar tamanho antes de ler; usar streaming/um arquivo por vez onde compatível com validação e transporte.
- Definir política explícita de duração/tamanho/eventos e retenção local, sem descartar dados silenciosamente nem apagar pendências.
- Validar uma Development Build no equipamento real: memória total/gerenciada/nativa, GC.Alloc, tempo de frame durante coleta e finalização, sessões consecutivas, maior duração esperada, upload simultâneo e falta de rede/espaço. Não usar GC.Collect periódico como solução para objetos ainda referenciados.

Nenhum script de produção ou configuração de cena foi alterado nesta análise.