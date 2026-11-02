# Integração de participantes no MainMenu — análise e plano incremental

Estado: análise inicial preservada abaixo. A etapa 4 está descrita em [ConsultaParticipantesSupabase.md](ConsultaParticipantesSupabase.md). A interface, validação do início e persistência local foram posteriormente integradas à cena existente conforme [MainMenuParticipantesConfigurado.md](MainMenuParticipantesConfigurado.md). A consulta real e a escrita remota de sessões continuam dependendo da autorização ausente no SQL fornecido.

## 1. Estrutura encontrada

Projeto: Unity 6000.3.14f1, XR Interaction Toolkit 3.3.1, uGUI 2.0.0 e Input System 1.19.0, conforme ProjectVersion.txt e Packages/manifest.json.

Hierarquia relevante extraída de Assets/Scenes/MainMenu.unity:

```text
MenuScript                              MainMenu
Menu
  Canvas                                World Space, CanvasScaler,
    GameObject                          GraphicRaycaster e TrackedDeviceGraphicRaycaster
      Text (TMP)                        Título
    Buttons
      Panel
      Vertical                          VerticalLayoutGroup
        ButtonIniciar                   Button + Image
          Text (TMP)
        ButtonSair                      Button + Image
          Text (TMP)
Sobre
  Canvas                                World Space e raycasters
    Buttons
      Panel
      Text (TMP)
EventSystem                             EventSystem + XRUIInputModule
XR Origin (VR)                          Instância do prefab existente
Canvas                                  Canvas adicional em Screen Space Overlay
```

O conteúdo visual do menu usa uGUI e TextMeshProUGUI. Não há UI Toolkit nessa interface. O EventSystem habilita entradas XR e mouse. O prefab do XR Origin possui XRRayInteractors com interação de UI habilitada. Esses dados indicam uma base adequada para o novo painel; o funcionamento precisa ser validado em Play Mode e no headset.

### Componentes reutilizáveis

| Componente existente | Uso atual | Integração prevista |
| --- | --- | --- |
| MainMenu, em MenuScript | IniciarSimulador carrega SampleScene; Sair encerra a aplicação | Preservar métodos públicos e adicionar a validação ao caminho de início |
| Menu/Canvas | Interface em World Space | Hospedar o novo painel no mesmo Canvas |
| Buttons/Vertical | Organiza Iniciar e Sair | Adicionar um botão Participantes com o mesmo estilo |
| ButtonIniciar | OnClick aponta para MainMenu.IniciarSimulador | Preservar o ponto de entrada e controlar interactable |
| ButtonSair | OnClick aponta para MainMenu.Sair | Manter comportamento |
| EventSystem e raycasters | Encaminham interação XR/uGUI | Reutilizar, sem criar outro EventSystem |
| AboutMenu, Rotator e Outline | Interação com a protoboard e painel Sobre | Preservar; AboutMenu é um XRSimpleInteractable específico, não um gerenciador genérico de painéis |
| TelemetrySessionManager | Identidade, gravação, eventos, métricas e salvamento local | Reutilizar ConfigureParticipant e adaptar a entrada do UUID da sessão |
| TelemetryHeadRecorder e TelemetryControllerRecorder | Amostragem de cabeça e controles | Manter na cena do simulador com os Transforms atuais |
| AutoTelemetrySession | Inicia gravação após aguardar um frame | Configurar a identidade e associar a sessão antes de iniciar os gravadores |
| TelemetryLocalStorage e TelemetrySessionPayload | Session.json e CSVs locais | Preservar formatos e preencher com a mesma identidade da sessão selecionada |
| SessionMetricsManager | Atualiza UI e envia tentativas à telemetria | Preservar referências e contagem de tentativas |

### Conflitos confirmados nos arquivos

1. IniciarSimulador chama SceneManager.LoadScene diretamente, sem validar participante ou sessão. Desabilitar apenas o Button não protegeria chamadas por outros scripts.
2. TelemetrySessionManager.Awake gera um participante anônimo. O fluxo integrado precisará adotar o UUID selecionado antes do início da gravação.
3. BeginSession gera um novo UUID de sessão internamente. Usá-lo sem adaptação produziria uma identidade diferente da sessão preparada no menu.
4. SampleScene contém TelemetrySystem com AutoTelemetrySession ativo. TelemetrySessionInput está desabilitado. A associação precisa ocorrer antes da chamada automática a BeginSession, sem depender de uma corrida entre Start de componentes distintos.
5. SupabaseManager envia somente para a RPC salvar_telemetria_completa. Não há leitura de participantes ou gerenciamento remoto de sessões nesse script.
6. A URL padrão de SupabaseManager já termina em /rest/v1/, mas rpcEndpoint também começa com /rest/v1/. A concatenação atual duplica esse trecho. O transporte compartilhado deverá normalizar a URL em uma etapa própria.
7. Não foi encontrada instância de SupabaseManager nas cenas/prefabs pesquisados, nem chamada externa a SendTelemetryData nos scripts de Assets. A gravação atual termina em TelemetryLocalStorage; não há encadeamento automático de upload identificado.
8. SimulationEvents declara eventos de ciclo de vida, mas não há chamadas externas ou assinantes encontrados. Não se pode presumir que esses eventos já coordenem a sessão.
9. EditorBuildSettings.asset lista apenas SampleScene habilitada. MainMenu precisará ser incluída como primeira cena no perfil de build utilizado para funcionar como entrada da aplicação.
10. Os UnityEvents da cena registram MainMenu, Assembly-CSharp como tipo do alvo, enquanto o script atual está no namespace MainProjeto.MenuPrincipal.Scripts. Validar e, se necessário, reassociar os callbacks no Inspector quando a cena for alterada; a inspeção textual não demonstra por si só falha em execução.

## 2. Arquitetura proposta

### Novos componentes e responsabilidades

| Componente proposto | Responsabilidade |
| --- | --- |
| SupabaseConnectionSettings | ScriptableObject com URL do projeto e chave pública; sem estado de participante e sem credenciais administrativas |
| SupabaseService | Transporte REST com UnityWebRequest, operações assíncronas, timeout, cancelamento e resultado tipado |
| ParticipantRepository | Consulta paginada e ordenada; mapeamento do esquema confirmado para UUID e nome; validação da resposta |
| Participant | Modelo simples com UUID e nome, sem dependência de UI |
| ParticipantManager | Seleção por UUID e notificação de mudança; classe comum, sem outro singleton |
| SessionContext | UUID, UUID do participante e estado local da sessão |
| ApplicationSessionContext | Único MonoBehaviour persistente que possui a seleção e o contexto da sessão; rejeita instâncias duplicadas |
| SessionRepository | Comunicação com a tabela/RPC de sessões, depois de confirmado o contrato |
| SessionCoordinator | Prepara e associa sessão, evita operações concorrentes e coordena transições de estado |
| ParticipantSelectionUI | Carregamento, lista vazia, erro, atualizar e voltar; apresenta dados e emite a seleção |
| ParticipantListItemUI | Exibe nome, ação Selecionar e indicação persistente da seleção por UUID |
| MenuIntegrationController | Conecta UI, seleção, sessão e validação do início; mantém serviços independentes do menu |

O SupabaseManager existente será adaptado gradualmente para delegar ao transporte compartilhado, preservando SendTelemetryData. Ele não será instanciado como um segundo serviço persistente. A integração não requer instalar um SDK C# externo para fazer as operações REST.

Dependências previstas: UI emite intenção ao controlador; controlador usa ParticipantManager e SessionCoordinator; repositórios usam SupabaseService. O transporte não conhece UI, gravadores ou MainMenu. O contexto persistente mantém somente dados e notificações, sem referências a Canvas, câmeras ou controles.

### Fluxo previsto

1. Abrir MainMenu inicializa o contexto único e consulta a lista no fundo. O início permanece bloqueado sem seleção válida.
2. O botão Participantes abre o novo painel. A lista usa UUID como chave; nomes iguais são permitidos. Nome e um identificador curto podem distinguir visualmente homônimos, mantendo o UUID completo internamente.
3. Selecionar preenche ParticipantManager e prepara um contexto local de sessão. Nenhuma gravação começa no menu. Trocar de pessoa antes de iniciar substitui a preparação local.
4. Voltar fecha o painel e mantém a identificação visível no menu. Iniciar fica disponível com seleção válida e nenhuma operação de início em andamento.
5. IniciarSimulador passa pela validação do controlador. O início bloqueia imediatamente novos cliques, confirma/cria a sessão remota e só então carrega SampleScene de forma assíncrona. Falhas mantêm o usuário no menu com opção de tentar novamente.
6. Tentativas repetidas reutilizam a mesma chave de idempotência/UUID. O banco deve garantir unicidade e a RPC/insert deve respeitar essa chave. Um bloqueio no cliente sozinho não evita duplicação após timeout de uma requisição já confirmada pelo servidor.
7. AutoTelemetrySession recebe participante e sessão do contexto antes de BeginSession. TelemetrySessionManager registra o mesmo UUID, sem criar outra sessão. O estado passa a ativo quando os gravadores iniciarem com sucesso.
8. Encerramento e falha de salvamento atualizam estados distintos. Salvamento local não será apresentado como confirmação de upload. O envio de arquivos/telemetria completos permanece uma responsabilidade separada do cadastro de sessões.

Os estados propostos são Preparada, Associando, Pronta, Ativa, Encerrada e SalvamentoPendente. O mapeamento para os estados do banco depende do esquema real. Se o carregamento de SampleScene ou o início dos gravadores falhar após a confirmação remota, manter o UUID e apresentar recuperação; não gerar outra sessão automaticamente.

### Persistência e ciclo de vida

ApplicationSessionContext será um GameObject raiz dedicado com DontDestroyOnLoad e proteção contra duplicações. Ele preservará UUID e nome do participante, UUID da sessão e estado. Managers de gravação continuam locais à cena porque dependem dos Transforms do XR Origin daquela cena.

O retorno ao menu exige encerrar/resolver a sessão anterior antes de trocar de pessoa. Novas tentativas de salvar mantêm o UUID e o payload pendente atuais. Evitar apagar a identificação de uma sessão que ainda tem dados por salvar. Persistência entre cenas não implica restauração após fechar o aplicativo; eventual retomada após reinício será definida separadamente.

## 3. Estrutura proposta de arquivos

```text
Assets/MainProjeto/Telemetria/
  Supabase/
    SupabaseManager.cs                    existente, fachada do envio atual
    SupabaseConnectionSettings.cs         configuração pública
    SupabaseService.cs                    transporte assíncrono compartilhado
  Participants/
    Participant.cs                       modelo
    ParticipantRepository.cs             consultas de participantes
    ParticipantManager.cs                seleção
  Sessions/
    ApplicationSessionContext.cs         objeto persistente único
    SessionContext.cs                    dados e estado
    SessionRepository.cs                 persistência remota da sessão
    SessionCoordinator.cs                ciclo de vida e idempotência
  Telemetry/
    Core/                                gravadores e fluxo atuais preservados
    Data/
    Recorders/
    Persistence/

Assets/MainProjeto/MenuPrincipal/
  Scripts/
    MainMenu.cs                          ponto de entrada existente
    MenuIntegrationController.cs         validação e navegação
    ParticipantSelectionUI.cs            painel
    ParticipantListItemUI.cs             item da lista
  Prefabs/
    ParticipantListItem.prefab           Button/Image/TMP no estilo atual
```

Esses nomes identificam arquivos futuros; não indicam componentes já criados.

## 4. Configuração prevista no Inspector, por incremento

### Etapa 4 — conexão e consulta

Criar um asset de SupabaseConnectionSettings com a Project URL normalizada e a chave pública existente, sem copiar valores para documentação ou logs. Configurar o contrato real do repositório: schema, tabela/view, coluna UUID, coluna nome e ordenação estável. Suportar paginação para não apresentar como completa uma resposta limitada pelo servidor.

Usar apikey para a chave pública e, quando houver autenticação do responsável, Authorization com o JWT de acesso desse responsável. Uma chave publishable não é um JWT de usuário. Não gravar senha ou token do operador no asset. A política do projeto define se a listagem exige usuário autenticado ou permite anon; não ampliar RLS para contornar erro de acesso.

### Etapa 5 — interface

Adicionar ParticipantSelectionPanel como filho de Menu/Canvas, preservando posição, escala e componentes do Canvas. Adicionar o botão Participantes ao Vertical existente; não reutilizar Sair como navegação interna.

O painel conterá ScrollRect com Viewport/RectMask2D, Content com VerticalLayoutGroup e dimensionamento apropriado, texto TMP de estado, identificação da seleção e botões Atualizar/Voltar. Cada item terá nome TMP, Button e indicador de seleção. Não depender somente da cor temporária de foco do EventSystem para mostrar quem foi selecionado.

ParticipantSelectionUI receberá referências serializadas para Content, prefab do item, texto de estado, identificação da seleção e botões. O controlador receberá o grupo Buttons atual e o novo painel para alternar visibilidade. Reutilizar os raycasters do Canvas e o EventSystem. Testar rolagem pelo mouse e pelos controles XR existentes.

### Etapa 6 — seleção e sessão

Adicionar o contexto persistente uma vez na raiz da cena, com proteção de duplicação. Instanciar as classes de seleção e sessão pelo ponto de composição da integração. A interface observará mudanças e atualizará a marcação dos itens pelo UUID.

Atualizar cancela/substitui a consulta anterior e ignora respostas antigas. Falha ao atualizar não deve anunciar que o participante foi excluído. Se uma consulta completa confirmar que ele não está mais disponível, invalidar a seleção preparada e exigir nova escolha antes do início.

### Etapa 7 — início existente

Adicionar MenuIntegrationController ao MenuScript e atribuir MainMenu, ButtonIniciar, painel, grupo de botões, texto de seleção/status e configuração do Supabase. Preservar o callback público MainMenu.IniciarSimulador e fazer esse método passar pelo controlador antes da transição. A validação deve valer para chamadas de código e UnityEvent. Conferir callbacks no Inspector e manter Sair.

### Etapa 8 — associação à telemetria e validação

Adaptar AutoTelemetrySession no TelemetrySystem da SampleScene para configurar a identidade e usar a sessão preparada antes de começar. A alteração em TelemetrySessionManager será pequena e explícita: aceitar o UUID associado, conservar proteção contra gravação/salvamento pendente e manter o fluxo independente para testes diretos quando configurado. O caminho iniciado pelo menu sempre exige identidade válida.

Incluir MainMenu antes de SampleScene no Build Profile usado. Validar no Editor e no headset:

- Consulta normal, lista vazia, timeout, falta de rede, 401/403 e JSON inválido.
- Participantes homônimos com UUIDs distintos e listas maiores que uma página.
- Atualizações consecutivas sem linhas duplicadas nem resposta antiga sobrescrevendo a nova.
- Voltar durante consulta sem callbacks sobre objetos destruídos.
- Nenhum participante selecionado bloqueia tanto ButtonIniciar quanto a chamada direta de IniciarSimulador.
- Clique repetido e retry após timeout geram uma única sessão no banco.
- UUID e nome do contexto chegam ao payload da telemetria; UUID da sessão coincide com o confirmado pelo servidor.
- Gravação automática só começa após a associação da identidade.
- Falha ao carregar a cena ou ao iniciar gravadores preserva uma recuperação com o mesmo UUID.
- Retornar ao menu não duplica contexto nem perde dados pendentes.
- Sobre, Sair, métricas e interações XR mantêm o comportamento observado antes da integração.
- Nenhuma chave administrativa no build e nenhuma credencial/dado pessoal em mensagens de erro de rede.

## 5. Contrato do banco recebido após a análise

O SQL 002_schema_modular.sql fornecido pelo usuário confirma public.participants(participant_code text PK, display_name text nullable) e public.sessions(session_id text PK, participant_code FK, started_utc obrigatório, ended_utc opcional e métricas). Os identificadores seguem o formato GUID N pelo contrato descrito nos comentários. Não existe coluna de disponibilidade de participante nem coluna de estado de sessão.

O SQL revoga privilégios de anon e authenticated e habilita RLS sem criar política de leitura. Isso mantém a consulta bloqueada pela API até definir quem é operador e como será autenticado. Não basta usar a chave pública ou fazer login sem a autorização correspondente.

Ainda são necessários o fluxo de autorização do responsável e, quando houver upload completo, a assinatura de salvar_telemetria_completa. A etapa 4 implementa a consulta contra o esquema confirmado e trata acesso negado sem mudar permissões. Não foram consultados dados do banco real.

## Referências

- Código e YAML locais mencionados neste documento.
- Documentação de UI do pacote XRI 3.3.1 instalado: Library/PackageCache/com.unity.xr.interaction.toolkit@5f736ad4ccd8/Documentation~/ui-setup.md.
- [Supabase: API keys](https://supabase.com/docs/guides/getting-started/api-keys) — separação entre chave pública da aplicação, identidade autenticada e credenciais administrativas.
- [Supabase: Row Level Security](https://supabase.com/docs/guides/database/postgres/row-level-security) — acesso condicionado às políticas do banco.
- [Supabase: API routes](https://supabase.com/docs/guides/api/creating-routes) — endpoints REST gerados a partir do esquema real.

Verificação realizada nesta etapa: leitura dos scripts, resolução dos GUIDs relevantes e inspeção da hierarquia e referências serializadas das duas cenas. Não houve compilação, Play Mode ou teste em headset, pois esta etapa não altera código executável.
