# Organizar as tarefas dos LEDs com botao

Os scripts agora seguem esta ordem: pressionar botao -> validar conexao/pinos/polaridade do LED -> ativar emissao -> confirmar a tarefa de pressionar -> concluir a tarefa de acender. Com `Require Led Confirmation` marcado, pressionar sem um LED valido nao conclui nenhuma dessas duas tarefas.

`PushButtonSwitch.ConfirmLedLit()` faz a confirmacao; `ConfirmLed()` foi mantido como alias para nao quebrar eventuais UnityEvents existentes. O comportamento com `Require Led Confirmation` desmarcado continua concluindo a tarefa diretamente no pressionamento.

## Campos no Inspector da SampleScene

| Objeto/componente | Campo | Asset |
| --- | --- | --- |
| led_red / LedController | Led Task | FaseLED2_3 |
| led_red / LedController | Light Up Task | FaseLED2_7 |
| led_red / LedController | Push Button Switch | PushButtonSwitch do pushButton_red |
| pushButton_red / PushButtonSwitch | Press Task | FaseLED2_4 |
| pushButton_red / PushButtonSwitch | Require Led Confirmation | Marcado |
| led_blue / LedController | Led Task | FaseLED2_5 |
| led_blue / LedController | Light Up Task | FaseLED2_8 |
| led_blue / LedController | Push Button Switch | PushButtonSwitch do pushButton_blue |
| pushButton_blue / PushButtonSwitch | Press Task | FaseLED2_6 |
| pushButton_blue / PushButtonSwitch | Require Led Confirmation | Marcado |

Preencha no componente correto da instancia/prefab em uso. Se editar a instancia da SampleScene, as mudancas sao overrides dessa cena; aplique ao prefab apenas se desejar reutiliza-las. Os scripts foram ajustados, mas nao houve alteracao automatica de cenas, prefabs ou assets de tarefas nesta entrega.

## Dependencias recomendadas

Os assets ficam em `Assets/MainProjeto/Quest/Task/2_FaseLEDcomBotao/Fase 2`. Configure a lista **Dependencies** de cada asset:

| Asset | Acao | Dependencies |
| --- | --- | --- |
| FaseLED2_0 | Falas iniciais | Nenhuma |
| FaseLED2_1 | Colocar resistor | _0 |
| FaseLED2_2 | Colocar botao vermelho | _1 |
| FaseLED2_3 | Colocar LED vermelho | _1 |
| FaseLED2_4 | Pressionar botao vermelho | _2 e _3 |
| FaseLED2_5 | Colocar LED azul | _0 |
| FaseLED2_6 | Pressionar botao azul | _5 |
| FaseLED2_7 | Confirmar LED vermelho aceso | _4 |
| FaseLED2_8 | Confirmar LED azul aceso | _6 |
| FaseLED2_Final | Avancar apos os dois LEDs acenderem | _7 e _8 |

As dependencias atuais adicionais de _7/_8 (resistor/LED/botao) podem permanecer, desde que essas tarefas sejam concluidas antes do pressionamento. As listas acima evitam repeticoes transitivas. Nao coloque _7 como dependencia de _4, nem _8 como dependencia de _6: isso bloquearia o botao que precisa gerar o acendimento.

Inclua os dez assets na lista **Tasks** de `03_FaseLedBotao_2`. A disponibilidade vem de Dependencies, nao apenas da ordem visual da lista.

## TaskCompleteListener

- No LED vermelho, o listener que controla a manipulacao acompanha _3; no azul, _5.
- No pushButton_red, o listener que libera a interacao acompanha _4; no pushButton_blue, _6.
- Nao precisa adicionar listeners de _7/_8 para concluir essas tarefas: o LedController ja chama CompleteTask.
- Para um evento On Complete adicional de _7/_8, pode usar outro listener com **Lock Interactable desmarcado**. Nao use esse evento para concluir novamente a mesma tarefa.

## Auto Connect e tarefas de colocar

No prefab original, os LEDs estavam configurados com Auto Connect. Para um LED que o participante deve colocar manualmente, desmarque **Auto Connect On Awake** nesse LED. Mantenha a configuracao de pecas que devem comecar prontas.

Uma peca preconectada pode tentar concluir sua tarefa em Start antes de as dependencias dessa tarefa estarem prontas. O modelo rejeita essa conclusao, e o acendimento nao conclui a tarefa de colocar automaticamente. Portanto, para uma montagem predefinida, nao deixe uma tarefa de colocar pendente sem um mecanismo separado que a conclua quando estiver disponivel. Os ajustes desta entrega nao modificaram Start/InstantConnect nem o tutorial.

## Conferir em Play Mode

1. Antes de montar o LED corretamente, pressionar nao deve concluir _4/_7 ou _6/_8.
2. Conclua os pre-requisitos de montagem. Pressione o botao vermelho: o LED deve acender e concluir _4 antes de _7.
3. Repita no azul: _6 antes de _8. Repetir um pressionamento nao deve repetir a conclusao.
4. FaseLED2_Final so fica disponivel depois de _7 e _8. Soltar um botao apaga seu LED, mas nao desfaz a tarefa ja concluida; nao e necessario segurar os dois simultaneamente.

A compilacao e os testes locais foram executados fora do motor. O acendimento visual e a interacao fisica devem ser conferidos em Play Mode/VR.
