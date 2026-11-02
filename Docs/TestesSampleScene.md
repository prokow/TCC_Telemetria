# Verificacao das interacoes e quests da SampleScene

Atualizacao posterior: a cena agora possui 32 tarefas e recebeu protecoes adicionais de narrativa/avanco. Os resultados e o roteiro atuais estao em `Docs/NarrativasQuestsECsvMovimentos.md`. Os numeros abaixo registram a verificacao anterior.

Foram analisados os arquivos reais de `Assets/Scenes/SampleScene.unity`, seus prefabs e os assets de quests/tarefas. A cena nao foi regravada, nao houve alteracao do XR Origin e nenhuma credencial/rede Supabase foi usada nesses testes.

## Problemas encontrados e corrigidos

1. **Segunda fase do divisor de tensao:** a primeira tarefa de `05_FaseDivisorTensao_2.asset` apontava para `FaseLED2_0`. O robo dessa fase conclui `FaseDV_2 0`, e o resistor depende desta tarefa. A referencia foi corrigida para `FaseDV_2 0`; antes havia uma dependencia externa ignorada pelo modelo e a tarefa incorreta podia ficar sem conclusao.
2. **Encaixe em pais deslocados/rotacionados:** `ProtoboardInteractor` passava `localPosition` para `Grid.WorldToCell`, que recebe coordenadas mundiais. Agora utiliza `position`, inclusive no fallback de alvos que nao sao EletronicComponent.
3. **Celulas presas depois de retirar uma peca:** a saida recalculava o footprint usando a posicao atual. Agora um snapshot das celulas reservadas na entrada e liberado independentemente de mover/rotacionar/reparentar a peca. `ProtoboardOccupancyMap` separa essa responsabilidade e permite testes executaveis sem o motor.
4. **Pecas com varios colliders:** a saida de um unico collider retirava o alvo inteiro. Agora os contatos sao acompanhados por collider e o alvo permanece ate sair o ultimo. Alvos destruidos/desativados e attach points sem uso sao removidos; desativar a protoboard limpa o estado.
5. **Callbacks de tarefa:** o evento de uma tarefa era disparado antes de ela ser marcada como concluida. O estado agora e atualizado antes do callback, que pode consultar a conclusao sem receber um resultado antigo.

As regras de pinos, validacao de componentes, `InstantConnect` e `autoConnectOnAwake` foram preservadas.

## Resultado local executado

| Verificacao | Resultado |
| --- | --- |
| Referencias locais da cena, prefabs, circuitos e dependencias | 8.662 verificacoes, zero falhas |
| Grafo das quests reais | 7 quests, 30 tarefas distintas; dependencias internas e sem ciclos; tarefas referenciadas nos respectivos circuitos |
| Geometria, estados do limite e reservas de celulas | 53 verificacoes aprovadas, usando as classes reais |
| Associacao entre quests e coleta de telemetria | 13 verificacoes aprovadas |
| Regressao de participante/sessao/telemetria | 338 verificacoes aprovadas, sem comunicacao com o banco real |
| Compilacao das fontes, Inspector e auditoria do Editor | Aprovada com as bibliotecas Unity/XRI instaladas |

O compilador externo mostra avisos existentes de campos nao usados e uma divergencia de versao de System.Collections.Immutable entre o SDK .NET e o pacote Unity AI. Isso e diferente de um teste de execucao no Unity.

A sequencia atual da cena foi preservada: Tutorial, Falas Iniciais, LED 1, LED 2, Divisor 1, Divisor 2, Falas Finais. A analise estatica comprova referencias e a possibilidade de ordenar as dependencias; nao comprova que uma pessoa concluiu as tarefas fisicamente.

## Repetir os testes locais

Na pasta do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\SampleSceneChecks\Run.ps1
```

Os resultados ficam em `Temp/SampleSceneChecks`. `scene-assets-before.json` conserva as falhas do grafo antes da correcao; `scene-assets-report.json` apresenta o resultado atual. Os logs de cada comando tambem sao gravados ali.

## Teste adicional com a API real do XR no Editor

Foi acrescentado o comando **Tools > Circuitos VR > Testar SampleScene sem Play Mode**. Aguarde o Unity importar/compilar os scripts e execute-o **fora do Play Mode**. Ele:

- abre uma copia de preview da SampleScene, resolvendo as instancias de prefabs pelo proprio Unity;
- verifica scripts ausentes, Rigidbody/colliders, referencias e caixas de limite;
- usa as quests/tarefas reais em um modelo temporario, verifica dependencias, conclusoes repetidas, visibilidade do estado nos callbacks e encerramento unico da ultima quest;
- cria fixtures isoladas com XRInteractionManager, ProtoboardInteractor, EletronicComponent e um seletor sintetico; testa encaixe mundial, varios colliders, retirada apos movimento, retorno, flags de queda, bloqueio de reagarrar e protecao de conexoes fora da caixa;
- captura erros do motor e grava `Temp/SampleSceneChecks/editor-report.json`, com `SAMPLE_SCENE_CHECKS: PASS/FAIL` no Console.

Nao salva a cena e nao entra/sai do Play Mode. O seletor sintetico usa os callbacks reais do XR, mas nao reproduz o hardware das maos. A fisica nao e avancada pelo executor, e a autoconexao via Start nao e exercitada por essas fixtures. **Nao houve confirmacao de execucao deste comando no ambiente desta entrega**; compilacao nao significa que esses casos passaram no motor.

## O que ainda conferir em Play Mode/VR

O Play Mode automatizado estava indisponivel por licenciamento no ambiente anterior; nao foi repetido sem evidencia de recuperacao. A verificacao fisica no Editor/headset permanece necessaria:

1. Iniciar a cena e conferir os pinos das pecas com Auto Connect antes de agarrar qualquer objeto.
2. Agarrar, girar, soltar e reconectar LED, resistor, botao e pontas do multimetro, respeitando polaridade e bloqueios de tarefa.
3. Com uma caixa atribuida em `movementArea`, testar as duas acoes fora de cada face; soltar o grip antes de pegar de novo. Confirmar queda e colisao com o chao.
4. Completar cada tarefa na ordem das dependencias, conferindo que o botao de proxima fase so e liberado no momento esperado.
5. Retirar uma peca e inserir outra nos mesmos pinos, e avancar a quest enquanto uma peca esta na mao: nao devem sobrar reservas/circuitos antigos.
6. Na ultima tarefa da ultima quest, conferir um unico evento de fim e encerramento da coleta. O envio real ao Supabase continua dependendo da migracao/configuracao ja documentada.

Pontas do multimetro conservam seu limite de cabo existente. O retorno/queda novo depende de configurar a referencia da caixa por componente; o comando do teste do Editor informa quantos componentes estao sem ela.
