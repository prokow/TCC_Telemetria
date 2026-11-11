# Menu direto de participantes — Windows / PC VR

A cena existente `Assets/Scenes/MainMenu.unity` abre no menu principal: título do simulador, **Iniciar simulador**, **Selecionar participantes** e **Sair do simulador**. Iniciar permanece desabilitado até escolher um cadastro válido. **Selecionar participantes** abre a lista no mesmo Canvas; ela mostra as pessoas e os botões **Atualizar** e **Voltar ao menu principal**. Voltar preserva a escolha e o menu identifica o participante selecionado.

O acesso da conta responsável é restaurado automaticamente e a lista vem diretamente de `public.participants` no Supabase. O frontend pode ficar fechado. A consulta é atualizada a cada cinco segundos; o login e sua renovação não mudam o painel que está aberto.

## Autorizar este computador uma vez

### Preencher diretamente no código

Abra `Assets/MainProjeto/MenuPrincipal/Scripts/AutomaticOperatorLogin.cs`. No início da classe, preencha `OperatorEmail` e `OperatorPassword` com a conta de Authentication que já cadastra participantes. Salve e dê Play na MainMenu ou gere uma nova build: o script faz o login automaticamente no Editor, no Windows e no Meta Quest/Android. Não é necessário preencher o Inspector ou executar PowerShell.

Essas duas variáveis entram no executável/APK: as credenciais podem ser extraídas de uma cópia da build. As credenciais digitadas no código também ficam no próprio arquivo-fonte; remova-as antes de compartilhar esse arquivo. Deixe ambas vazias para usar apenas a autorização já salva ou uma das opções abaixo. O login manual usa os campos da tela em `OperatorLoginUI.cs`; não preencha credenciais fixas nesse método.

### Preencher pelo Inspector (alternativa)

Você pode fazer isso inteiramente pelo Inspector, sem terminal e sem editar o código:

1. Feche outras cópias do simulador que estejam rodando neste computador.
2. Abra `Assets/Scenes/MainMenu.unity` e aguarde a compilação dos scripts.
3. Pressione **Play** e selecione **MenuScript** na Hierarchy.
4. No Inspector, encontre o componente **Automatic Operator Login (Script)** e a seção **Autorizar acesso automático**.
5. Preencha **E-mail do responsável** e **Senha** com a mesma conta de Authentication que já consegue cadastrar participantes. A senha fica oculta durante a digitação.
6. Clique em **Autorizar este computador**. Aguarde a mensagem de sucesso e a lista de participantes.
7. No menu, clique em **Selecionar participantes**, escolha a pessoa e use **Voltar ao menu principal**. Clique em **Iniciar simulador**, agora habilitado.

Esses campos são temporários do Inspector; não são campos serializados do MonoBehaviour. O e-mail e a senha não ficam na cena, no asset, no código-fonte nem no build. O campo Senha é limpo ao clicar no botão. A configuração usa o Gerenciador de Credenciais do Windows, assim como o configurador abaixo. Nos próximos acessos, basta iniciar a cena e escolher o participante.

O botão fica disponível apenas em **Play Mode no Editor Windows**, com o componente ativo e sem outro login em andamento. Se os campos não aparecerem, aguarde a compilação e verifique se o Inspector está no modo **Normal**. O arquivo que desenha esses campos é `Assets/MainProjeto/MenuPrincipal/Editor/AutomaticOperatorLoginEditor.cs`.

Como alternativa, o configurador PowerShell continua disponível. Saia do Play Mode antes de executá-lo na pasta do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ConfigurarLoginAutomatico.ps1
```

Digite a mesma conta do responsável, aguarde **Pronto!** e abra a MainMenu em Play Mode. Os dois configuradores usam a URL e a chave pública já existentes no asset Unity. O cadastro continua sendo feito no frontend; a Unity consulta os cadastros e seleciona a pessoa.

A configuração vale para o perfil Windows atual, neste computador, tanto no Editor quanto no executável Windows. Em outro perfil ou computador, execute novamente o configurador. Não execute duas cópias do simulador simultaneamente com a mesma autorização local: o Supabase rotaciona os tokens de renovação.

## Componentes e Inspector

As referências já estão configuradas na MainMenu; não é necessário criar GameObjects ou recolocar os componentes.

| Arquivo | Responsabilidade |
| --- | --- |
| `Assets/MainProjeto/MenuPrincipal/Scripts/AutomaticOperatorLogin.cs` | Restaurar a autorização, renovar antes da expiração, sinalizar conexão/erro e impedir renovações simultâneas. No objeto `MenuScript`, usa o asset `MenuSupabaseConnection` no campo `Settings`. |
| `Assets/MainProjeto/MenuPrincipal/Editor/AutomaticOperatorLoginEditor.cs` | Exibir e-mail, senha mascarada e o botão de autorização no Inspector em Play Mode. Os campos são temporários e esse script não entra no build. |
| `Assets/MainProjeto/Telemetria/Supabase/RememberedOperatorLogin.cs` | Carregar a autorização e salvar o refresh token rotacionado antes de liberar o acesso ao menu. |
| `Assets/MainProjeto/Telemetria/Supabase/WindowsSupabaseSessionStore.cs` | Guardar somente o refresh token no Gerenciador de Credenciais do Windows, separado por projeto Supabase. Implementa `ISupabaseSessionStore`. |
| `Assets/MainProjeto/Telemetria/Supabase/SupabaseAuthService.cs` | Fazer as chamadas assíncronas de autenticação e renovação, com tratamento de respostas inválidas e falhas. |
| `Assets/MainProjeto/MenuPrincipal/Scripts/MenuIntegrationController.cs` | Integrar autenticação, lista, seleção e o botão Iniciar existente. O campo `Automatic Login` referencia o novo componente no mesmo objeto. |
| `Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantSelectionUI.cs` | Renderizar a lista, destacar a pessoa escolhida e bloquear linhas inválidas. |
| `Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantAutoRefresh.cs` | Consultar o Supabase independentemente do frontend, sem reconstruir a lista quando ela não mudou. |
| `Tools/ConfigurarLoginAutomatico.ps1` | Autorizar o perfil Windows uma vez, sem colocar senha ou tokens em arquivos do projeto. |

Os botões **Iniciar**, **Selecionar participantes** e **Sair** pertencem a `MainMenuPanel`; os callbacks originais de Iniciar e Sair foram preservados. O painel `ParticipantSelectionPanel` contém a lista, Atualizar e Voltar, sem Iniciar ou Sair. O painel de login manual anterior permanece na cena, oculto. O Canvas World Space, os componentes XR e o carregamento de `SampleScene` foram reaproveitados.

## Sessões e próxima pessoa

O usuário de Authentication é o responsável autorizado a consultar o banco. O participante escolhido é uma linha de `participants`, identificado por `participant_code`; pessoas com nomes iguais continuam tendo identidades distintas.

O contexto persistente mantém participante, UUID da sessão e estado durante a transição para `SampleScene`. A coleta existente começa nessa cena e termina na última quest. Ao retornar à MainMenu após salvar a sessão, é necessário selecionar a próxima pessoa explicitamente. Cliques repetidos em Iniciar não criam outra sessão. Uma sessão ativa ou com salvamento pendente não pode ser descartada pelo menu.

O uploader posterior agora compartilha essa autenticacao entre cenas e envia JSON/CSVs finalizados ao Storage e resumos ao banco. A ativacao e os testes estao em [EnvioTelemetriaSupabase.md](EnvioTelemetriaSupabase.md); e necessario aplicar a migracao 007.

## Credenciais e permissões

Ao usar o configurador do Inspector ou PowerShell, a senha é utilizada para obter uma sessão de Authentication sem ser gravada no projeto ou no build. Se você optar por preencher as variáveis diretamente no código, a senha permanece nesse arquivo-fonte e também entra no executável/APK. O token de acesso fica em memória; o refresh token fica protegido pelo Gerenciador de Credenciais do Windows. Esse token concede o acesso da conta responsável a quem puder usar esse perfil Windows.

O Supabase continua aplicando as permissões e políticas RLS dessa conta. Não é necessário liberar leitura anônima nem usar `service_role`. As políticas SQL existentes não foram alteradas.

Se o acesso for revogado ou a sessão expirar conforme as configurações do Supabase, o aplicativo tenta autorizar novamente com as credenciais configuradas no código. Se elas estiverem vazias, execute o configurador novamente ou use o login manual. Se faltar a autorização, Iniciar permanece bloqueado. Falhas de rede são tentadas novamente automaticamente; uma falha de rede não apaga a autorização guardada.

Para remover a autorização local, com o simulador fechado:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ConfigurarLoginAutomatico.ps1 -Remover
```

Isso remove a autorização deste Windows, sem revogar outras sessões no servidor. Para revogar acesso ao dispositivo, use também o gerenciamento de sessões/permissões do Supabase.

## Compatibilidade e validação

O armazenamento Windows atende **Play Mode e PC VR**. Para APK executado diretamente no **Meta Quest/Android**, o projeto seleciona armazenamento protegido por Android Keystore. Com `OperatorEmail` e `OperatorPassword` preenchidos, o headset faz o login automaticamente. A autorização Windows não é copiada para o APK. Consulte [Login no Meta Quest](LoginMetaQuest.md) para configurar e gerar o novo APK.

Os testes locais compilam os scripts reais com as bibliotecas Unity e verificam renovação, rotação, cancelamento, falta de autorização, falhas de rede, UUIDs, sessão e sequência das quests. A validação da cena confere YAML, referências, callbacks e preservação dos objetos fora da integração. O configurador também pode ser verificado sem rede e sem acessar credenciais:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ConfigurarLoginAutomatico.ps1 -ConfigurationOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ParticipantIntegrationChecks\Run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ParticipantIntegrationChecks\CheckScene.ps1
```

Esses testes não substituem o teste em Play Mode com a conta real: autorize, confirme os nomes cadastrados, selecione uma pessoa, inicie e confira o UUID e o arquivo local da sessão ao concluir a última quest.

Para testar os botoes reais, a navegacao, selecao por UUID, erros e limites de layout em Play Mode com participantes ficticios e sem chamadas ao Supabase, ha um teste batch em copia isolada do menu:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ParticipantIntegrationChecks\RunCanvasPlayMode.ps1
```

Esse teste usa pacotes ja instalados e uma Library propria em `Temp/MenuCanvasPlayModeProject`. O resultado fica em `Temp/MenuCanvasPlayModeProject/MenuCanvasFlowChecks.log`, com o marcador `MENU_CANVAS_CHECKS: PASS` ou `FAIL`. As credenciais escritas no codigo original sao substituidas por campos vazios na copia; login automatico e consulta periodica ficam desativados na cena temporaria. O teste nao verifica controle XR fisico nem envia telemetria.
