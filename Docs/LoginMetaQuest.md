# Login automático no Meta Quest / Android

A mensagem **Este dispositivo precisa de um provedor de autorização compatível** vinha de `AutomaticOperatorLogin`, ao tentar usar `WindowsSupabaseSessionStore` no Android. É o login do cadastro de participantes no Supabase. A autorização salva no Windows não é transferida ao headset.

## Usar no headset

1. Aguarde o Unity importar e compilar os scripts e o plugin Java.
2. Configure `OperatorEmail` e `OperatorPassword` no início de `Assets/MainProjeto/MenuPrincipal/Scripts/AutomaticOperatorLogin.cs`. Use a conta do responsável no Supabase, não o login da Meta. Esses campos agora valem para o Editor e para as builds.
3. Gere um novo APK Android e instale no Quest. O APK anterior não recebe a alteração.
4. Com internet no headset, abra o aplicativo e toque em **Selecionar participantes**. O login começa automaticamente ao abrir o menu; a lista aguarda a conexão quando necessário, sem pedir para clicar em Entrar.
5. Escolha o participante e inicie o simulador.

As credenciais configuradas ficam incluídas no APK/executável e podem ser extraídas de uma cópia da build. Não compartilhe o código com a senha preenchida.

Se a renovação da sessão for recusada, o aplicativo tenta autorizar novamente com as credenciais configuradas. Uma falha de rede preserva a autorização e permite novas tentativas. Se as credenciais estiverem vazias ou forem recusadas, a tela de login manual continua disponível; `OperatorLoginUI.cs` lê os campos dessa tela, sem credenciais duplicadas no método `SignIn`.

## Implementação

`SupabaseSessionStore.Create` seleciona o armazenamento Android no APK e o armazenamento Windows no Editor/PC. `OperatorSessionStore.java`, importado apenas para Android, cifra o refresh token com AES/GCM e uma chave não exportável do Android Keystore; as preferências privadas do aplicativo recebem somente o valor cifrado. A rotação do token é salva antes de liberar a sessão. As credenciais fixas do login automático ficam no código compilado.

A tela de login existente usa `SharedSupabaseSession`, compartilhando a autorização com a restauração automática e o envio de telemetria. O menu abre o login quando falta autorização ou o Supabase recusa a renovação; uma conexão normal preserva o fluxo direto de participantes.

Mantenha o mesmo identificador Android e a mesma assinatura ao atualizar o APK para preservar os dados. Ao desinstalar o aplicativo ou limpar seus dados, o próximo acesso autoriza automaticamente com as credenciais configuradas. Cada headset faz seu próprio login; não copie refresh tokens de outro dispositivo.

O projeto atualmente está com minificação Android desativada. Se ativar R8/ProGuard, preserve a classe acessada por JNI com a regra `-keep class com.circuitosvr.auth.OperatorSessionStore { *; }` no arquivo ProGuard usado pelo build.

Referências: [Android Keystore](https://developer.android.com/privacy-and-security/keystore) e [plugins Java no Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/AndroidJavaSourcePlugins.html).

## Validação

Execute `Tools/ParticipantIntegrationChecks/Run.ps1` para as verificações C# e `Tools/ParticipantIntegrationChecks/RunCanvasPlayMode.ps1` para a navegação, incluindo o primeiro acesso sem autorização. Esses testes usam dados fictícios. A compilação Java pode usar o JDK e o `android.jar` do SDK Android instalado pelo Unity.

O ciclo completo de login, reabertura e renovação com Android Keystore precisa ser confirmado no headset: autorize uma vez, feche e reabra, confira a lista e conclua uma sessão de teste. O teclado virtual e o apontamento dos controles também dependem desse teste físico.

Na validação local desta correção, passaram 368 verificações C#, a compilação do armazenamento com `UNITY_ANDROID` e a compilação do plugin Java contra o Android SDK 35. O teste Play Mode não foi concluído por travamento na inicialização do Editor isolado. A checagem estática `CheckScene.ps1` também aponta uma divergência já existente na hierarquia do título/botões/resumo; a cena não foi alterada por esta correção.
