# Canvas de MainMenu: fluxo e validacao

O Canvas World Space existente agora tem dois paineis. A cena nao foi recriada e os objetos XR, os controles e o painel Sobre foram preservados.

- `MainMenuPanel`: titulo original do simulador, Iniciar simulador, Selecionar participantes, Sair do simulador e resumo do participante.
- `ParticipantSelectionPanel`: lista com nome e selecao por UUID, identificacao da escolha, mensagem de estado, Atualizar e Voltar ao menu principal.

Ao abrir a cena, o menu principal fica visivel e a lista comeca fechada. Iniciar fica bloqueado ate existir autenticacao valida, consulta confirmada e participante escolhido que ainda conste na lista. Voltar preserva a escolha. Atualizacoes manuais bloqueiam o inicio durante a consulta; falhas e remocao do cadastro tambem bloqueiam. A renovacao de login nao muda o painel aberto.

## Verificacoes realizadas

- Compilacao dos scripts reais do menu e dos testes com bibliotecas do Unity 6: passou.
- 300 verificacoes automatizadas de autenticacao, renovacao, repositorio, selecao por UUID, ciclo de sessao e conclusao das quests: passaram.
- Validacao YAML da cena, referencias do Inspector, hierarquia, rotulos, callbacks, medidas dos paineis, espaco entre botoes e preservacao dos objetos fora da integracao: passou.
- 13 verificacoes da ligacao entre quests e coleta na SampleScene: passaram.

O teste de Play Mode em uma copia isolada foi tentado. O Unity iniciou, mas o cliente de licenciamento nao concluiu a inicializacao (`Licensing initialization failed`). Portanto os cliques e o layout em execucao **nao foram confirmados por esse teste**. A tentativa no projeto original tambem foi recusada pelo Unity porque ele ja estava aberto em outra instancia. O Editor original nao foi encerrado.

O teste de Play Mode esta implementado em `Assets/MainProjeto/MenuPrincipal/Editor/MenuCanvasFlowChecks.cs`. O comando `Tools/ParticipantIntegrationChecks/RunCanvasPlayMode.ps1` monta uma copia em `Temp/MenuCanvasPlayModeProject`, sem as credenciais preenchidas no codigo. Ele desativa login e consulta periodica, injeta participantes ficticios e verifica botoes reais, navegacao, selecao, renovacao sem redirecionamento, carregamento, erro, recuperacao, remocao de cadastro e limites do layout. Apenas um marcador `MENU_CANVAS_CHECKS: PASS` no log confirma que ele concluiu.

## Conferencia no seu Editor

1. Reabra `Assets/Scenes/MainMenu.unity`, preservando eventuais edicoes proprias nao salvas, e de Play. Confira titulo e tres botoes; Iniciar deve estar desabilitado.
2. Clique em Selecionar participantes. Iniciar e Sair devem desaparecer; Atualizar e Voltar devem estar visiveis.
3. Selecione uma pessoa. A linha deve mostrar Selecionado. Clique em Voltar ao menu principal.
4. Confira o nome escolhido e o botao Iniciar habilitado. Abra a lista novamente, atualize e volte: a escolha deve continuar se o cadastro ainda existir.
5. Clique em Iniciar e confirme a abertura de SampleScene com a pessoa correta. Conclua a ultima quest e confira o salvamento local da sessao pelo fluxo de telemetria existente.

A conferencia com controles XR fisicos e a execucao completa com o Supabase real continuam dependendo do Editor/dispositivo e da conta configurada.
