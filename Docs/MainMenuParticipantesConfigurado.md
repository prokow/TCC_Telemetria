# Participantes integrados à cena MainMenu

A cena `Assets/Scenes/MainMenu.unity` foi adaptada diretamente. As referências dos componentes já estão preenchidas no arquivo da cena; não é necessário criar outro menu ou montar o painel manualmente no Inspector.

Atualização: o login do responsável também foi implementado. O roteiro atual está em [LoginSupabasePrimeiroTeste.md](LoginSupabasePrimeiroTeste.md). As seções abaixo descrevem a entrega inicial da lista e sessão local.

O Canvas agora organiza os elementos em `MainMenuPanel` (titulo, Iniciar, Selecionar participantes, Sair e resumo da escolha) e `ParticipantSelectionPanel` (lista, Atualizar e Voltar ao menu principal). Iniciar e Sair aparecem somente no painel principal. O login automatico mantem o painel atual, inclusive durante a renovacao da autorizacao. Consulte [LoginAutomaticoParticipantes.md](LoginAutomaticoParticipantes.md) para a configuracao atual e os comandos de teste.

## Fluxo disponível

1. Ao abrir MainMenu, a aplicação consulta `public.participants`, usando a conexão pública já configurada no asset MenuSupabaseConnection.
2. **Selecionar participante** abre o painel de lista no Canvas World Space existente. O painel apresenta carregamento, lista vazia ou erro de acesso/conexão.
3. Cada item apresenta nome, parte do identificador e ação **Selecionar**. O participante selecionado permanece identificado pelo texto **Selecionado** e pelo fundo amarelo, mesmo após o foco XR mudar de botão.
4. **Atualizar** consulta novamente. Uma falha não é interpretada como exclusão de uma pessoa; o início permanece bloqueado até uma consulta válida.
5. **Voltar ao menu** preserva a escolha. O menu mostra o nome selecionado e habilita **Iniciar Simulador** quando a pessoa ainda consta na lista validada.
6. Iniciar valida novamente a seleção, bloqueia cliques repetidos e carrega SampleScene de modo assíncrono.
7. A telemetria recebe a identidade selecionada e usa o UUID local de sessão preparado no menu. Os gravadores e seus Transforms permanecem em SampleScene.

## Hierarquia acrescentada

```text
MenuScript
  MainMenu                              existente; callback público preservado
  ParticipantSelectionUI               consulta e apresentação
  MenuIntegrationController            ligação ao menu
Menu/Canvas
  Buttons/Vertical
    ButtonParticipantes                 novo
    ButtonIniciar                       existente; começa desabilitado
    ButtonSair                          existente
  ParticipantSummary                    identificação no menu principal
  ParticipantSelectionPanel             começa fechado
    ParticipantTitle
    ParticipantStatus
    SelectedParticipant
    ParticipantScrollView               ScrollRect vertical
      Viewport                          RectMask2D
        Content                         VerticalLayoutGroup + ContentSizeFitter
          ParticipantItemTemplate       modelo inativo, clonado para cada participante
            ParticipantName
            SelectionAction
    RefreshParticipantsButton
    BackToMainMenuButton
ApplicationSessionContext               raiz persistente dedicada
```

O painel reutiliza fonte, material e estilo dos botões existentes, com escala compatível com o Canvas atual. O XR Origin, EventSystem, raycasters, painel Sobre e seus objetos de interação foram preservados. Não foi adicionado outro Canvas ou EventSystem.

MainMenu foi incluída antes de SampleScene em `ProjectSettings/EditorBuildSettings.asset`, para que o build padrão abra no menu. Verificar separadamente perfis de build personalizados que substituam essa lista.

## Componentes e referências no Inspector

Em **MenuScript > MainMenu**, Integration referencia MenuIntegrationController no mesmo GameObject. Os UnityEvents de Iniciar e Sair mantêm seus métodos públicos; o tipo serializado foi corrigido para incluir o namespace real do script.

Em **MenuScript > MenuIntegrationController**:

- Settings: `MenuSupabaseConnection.asset`, reutilizando a URL e a chave pública existentes.
- Selection UI: ParticipantSelectionUI no MenuScript.
- Participant Panel: Menu/Canvas/ParticipantSelectionPanel.
- Main Buttons e Main Title: grupos Buttons e GameObject que já existiam no Canvas.
- Open Participants Button: ButtonParticipantes.
- Start Button: ButtonIniciar existente.
- Selected Summary: ParticipantSummary.
- Simulator Scene: SampleScene.

Em **MenuScript > ParticipantSelectionUI**, Content, Item Template, Scroll Rect, Status Text, Selection Text, Refresh Button e Back Button já apontam para os elementos correspondentes do painel.

Em **ParticipantItemTemplate > ParticipantListItemUI**, Select Button, Name Text, Action Text e Background já estão associados. Manter esse modelo desativado: o código instancia e ativa as cópias conforme a consulta.

## Scripts completos

| Script | Responsabilidade |
| --- | --- |
| [MainMenu.cs](../Assets/MainProjeto/MenuPrincipal/Scripts/MainMenu.cs) | Mantém IniciarSimulador e Sair; direciona início à validação |
| [MenuIntegrationController.cs](../Assets/MainProjeto/MenuPrincipal/Scripts/MenuIntegrationController.cs) | Conexão, navegação, validação e carregamento da cena |
| [ParticipantSelectionUI.cs](../Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantSelectionUI.cs) | Carregamento assíncrono, lista, erros, atualização e retorno |
| [ParticipantListItemUI.cs](../Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantListItemUI.cs) | Nome, seleção por UUID e identificação visual persistente |
| [ParticipantManager.cs](../Assets/MainProjeto/Telemetria/Participants/ParticipantManager.cs) | Participante selecionado e notificações, sem dependência de UI |
| [SessionContext.cs](../Assets/MainProjeto/Telemetria/Sessions/SessionContext.cs) | UUID e estado da sessão local, com transições e bloqueios |
| [ApplicationSessionContext.cs](../Assets/MainProjeto/Telemetria/Sessions/ApplicationSessionContext.cs) | Único objeto persistente, proteção de duplicação e composição dos estados |
| [TelemetrySessionManager.cs](../Assets/MainProjeto/Telemetria/Telemetry/Core/TelemetrySessionManager.cs) | Adota participante/sessão do menu e mantém gravação e salvamento existentes |

O transporte, validação e repositório são os da [etapa 4](ConsultaParticipantesSupabase.md). A interface não imprime nomes ou credenciais no Console, e desabilita rich text nos nomes para que o texto cadastrado não altere a formatação.

## Persistência e sessão

ApplicationSessionContext usa DontDestroyOnLoad e rejeita instâncias duplicadas quando o menu é carregado novamente. Preserva participante, nome, UUID da sessão e os estados locais None, Prepared, Active, PendingLocalSave e SavedLocally. O singleton também é reinicializado ao entrar em Play Mode com domain reload desativado.

Selecionar uma pessoa prepara um UUID em formato N. Selecionar novamente a mesma pessoa antes de iniciar ou repetir uma tentativa de carregamento reutiliza esse UUID. A sessão passa a Active somente depois que ambos os gravadores iniciarem. Ao encerrar, passa por PendingLocalSave e só chega a SavedLocally após TelemetryLocalStorage salvar o payload.

Enquanto uma sessão estiver ativa ou tiver salvamento pendente, o contexto bloqueia troca de participante e preparação de outra sessão. Ao iniciar pelo menu, TelemetrySessionManager não cria uma identidade anônima alternativa. Abrir SampleScene isoladamente, sem o contexto do menu, preserva o modo de teste anônimo anterior.

A persistência é entre cenas durante a mesma execução do aplicativo. Ainda não implementa retomada após fechar o processo. O payload pendente continua pertencendo ao gerenciador de gravação existente; esse contexto não é um uploader nem uma fila persistente de dados pendentes. Encerrar e salvar antes de sair da cena do simulador continua necessário.

## Limite atual do Supabase

O SQL fornecido bloqueia SELECT em participants para anon e authenticated e não define uma política de operador. Com esse SQL, a cena deve mostrar **Acesso à lista não autorizado** e manter Iniciar desabilitado. Não foram alterados privilégios ou políticas do banco.

É necessário definir o login/permissão de operador ou um backend autorizado para obter a lista real. O controlador oferece `SetAccessToken(string token)` para um futuro fluxo de login fornecer/renovar o JWT em memória, sem salvar o token na cena ou no Inspector. Trocar o token invalida a consulta anterior e exige nova validação da lista.

O UUID e o estado da sessão são locais neste incremento. Não há INSERT remoto em public.sessions nem envio de arquivos/telemetria ao Supabase. O SQL não tem coluna de estado da sessão; os estados acima não são apresentados como confirmação do banco. A escrita remota e sua política continuam uma etapa separada.

## Verificações realizadas e testes manuais

- **122 verificações automatizadas** de configuração, paginação, identidade, cancelamento e transições de seleção/sessão, com transporte simulado.
- Compilação isolada dos scripts novos e modificados e suas dependências locais contra bibliotecas do Unity 6000.3.14f1, uGUI, TMP e Input System.
- Validação do YAML com o parser instalado no projeto, referências locais, GUIDs dos novos componentes, callbacks e integridade pai/filho.
- Comparação com a cena anterior: somente seis blocos existentes e a lista de raízes foram alterados; objetos do cenário, XR e Sobre permaneceram intactos. Os demais objetos são acréscimos.

Executar novamente a partir da raiz do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ParticipantIntegrationChecks/Run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ParticipantIntegrationChecks/CheckScene.ps1
```

Não foi executado Play Mode nem teste com headset/banco real nesta entrega. Para validar a interação:

1. Abra a cena atualizada no Unity. Se ela já estava aberta antes das alterações em disco, recarregue-a preservando eventuais edições próprias ainda não salvas.
2. Entre em Play Mode. Confirme Iniciar bloqueado, abertura do painel, mensagem de acesso negado com o SQL atual, Atualizar e Voltar funcionando, e preservação de Sair/Sobre.
3. Com autorização de leitura configurada, confira nomes repetidos, seleção visual, rolagem por mouse e XR, falha de rede e atualização sem respostas antigas sobrescreverem a consulta atual.
4. Escolha alguém e inicie. Compare Participants.Selected.ParticipantCode e Session.SessionId no contexto com ParticipantCode e CurrentSessionId no gravador da SampleScene e no Session.json salvo.
5. Confirme que cliques repetidos não duplicam contexto/sessão e que falha de salvamento impede trocar de pessoa até resolver os dados pendentes.
