# Projeto Supabase correto

A Project URL confirmada pelo responsável é `https://qvszuzmwyfuyydfjjvxb.supabase.co`. Esse domínio resolveu no teste DNS; o domínio antigo retornou NXDOMAIN. A chave anon antiga também identificava o projeto antigo e foi removida do frontend, asset do Unity e configuração padrão de SupabaseManager.

## Correção do frontend após os testes de rede

O responsável confirmou DNS OK, HTTPS_AUTH HTTP 200 e PREFLIGHT_CORS HTTP 200, com origem e cabeçalhos permitidos. A análise identificou um defeito independente na chamada de fetch: a função nativa era armazenada sem bind e invocada como método de ParticipantClient, com o receptor incorreto. Navegadores podem lançar Illegal invocation nessa situação antes de qualquer requisição; o catch genérico convertia isso em mensagem de falha de conexão.

A chamada foi corrigida para `fetchImpl.bind(globalThis)`. Dois testes reproduziram a mensagem anterior ao exigir o receptor global e falharam antes da correção; depois, passaram junto com a suíte completa. Essa regressão não era coberta pelo fetch do Node, que tolera outro receptor.

Depois de atualizar os arquivos, manter o servidor aberto e recarregar **http://127.0.0.1:4173** com **Ctrl+F5**, usar Verificar conexão e entrar novamente. Não é necessário reconfigurar a chave, mudar políticas ou ativar Play Mode para esse teste. A correção foi verificada com um transporte que reproduz a restrição do navegador; login real e navegador gráfico não foram executados pelo agente.

O login precisa da **chave pública desse novo projeto**, além da URL. Após o responsável executar o configurador, foi confirmada uma chave Publishable gravada e idêntica no frontend, asset do Unity e SupabaseManager. A página servida em 127.0.0.1:4173 também apresentou essa mesma configuração. Isso confirma a gravação; a aceitação da chave ainda depende da resposta do servidor.

A chave fica salva nos arquivos e não precisa ser digitada a cada uso. Executar o configurador novamente é necessário apenas para trocar o projeto ou a chave. Publishable é uma chave pública própria para aplicações distribuídas; a autorização dos dados continua sendo feita pelas políticas e pelo login do responsável.

## Investigar falha de conexão após configurar

O frontend agora tem **Verificar conexão** na tela de login. Esse botão faz somente GET em /auth/v1/health com a chave pública; não envia e-mail, senha ou token de usuário. Diferencia serviço acessível, acesso/chave recusados, timeout e falha de acesso pelo navegador.

Recarregar a página com **Ctrl+F5** para obter o botão novo. Se o navegador ainda não alcançar o serviço, abrir **F12 > Console** e copiar somente a mensagem em vermelho, sem senha, tokens ou corpo de requisições. O browser pode esconder do JavaScript o motivo detalhado de DNS, TLS ou CORS; não interpretar genericamente esse caso como senha incorreta.

Na investigação feita pelo agente, o pedido externo recebeu EACCES do ambiente de execução. Isso impediu validar a conexão real por esse processo e não comprova um problema na rede do responsável. A leitura local de /config.js funcionou e confirmou URL/chave consistentes.

O configurador foi salvo com UTF-8 BOM para que o Windows PowerShell 5 leia corretamente os acentos. Isso corrige a exibição das mensagens do script, sem alterar a chave já gravada.

## Teste pelo PowerShell quando a mensagem da página não esclarecer a causa

Em outro PowerShell, executar a partir da raiz do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TestarConexaoSupabase.ps1
```

O teste lê a configuração já gravada, resolve o DNS, consulta a saúde da autenticação com a chave pública e verifica uma requisição OPTIONS equivalente à preparação do login pelo navegador. Não envia e-mail/senha, não executa login, não grava dados no banco e não altera rede, firewall ou certificados. Mantém a validação normal de TLS e oculta a chave nos resultados.

Copiar o texto do resultado para o chat. DNS, HTTPS_AUTH e PREFLIGHT_CORS ajudam a separar resolução de endereço, acesso HTTPS e permissões do navegador. A aprovação dessas verificações não valida a senha nem garante que extensões ou políticas do navegador permitam a requisição final.

## Configurar sem enviar a chave pelo chat

1. No projeto correto, abra **Settings > API Keys** e copie **Publishable key** (`sb_publishable_...`). Não use Secret, service_role ou senha de banco.
2. No PowerShell, a partir da raiz do projeto Unity, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ConfigurarSupabase.ps1
```

3. Cole a chave quando o script solicitar e pressione Enter. A entrada fica oculta. O script atualiza frontend e Unity juntos, sem exibir a chave e sem alterar o banco remoto.
4. Com o servidor do frontend aberto, use **Ctrl+F5** no navegador e teste o login com a conta criada no mesmo projeto.

O script aceita uma chave anon legada somente se o campo ref identificar o projeto correto. As Publishable keys são opacas; a associação ao projeto será validada pelo servidor ao entrar. O script nunca solicita ou grava a senha do usuário.

No Unity, a configuração fica em `Assets/MainProjeto/Telemetria/Supabase/Settings/MenuSupabaseConnection.asset`. No frontend, fica em `Web/participantes-vr/dist/config.js`. Senhas e tokens de usuário continuam somente em memória durante o uso.

Se o frontend estiver sendo executado de outra pasta extraída do ZIP, essa outra cópia também precisa receber a configuração correta. Prefira executar o frontend da pasta Web/participantes-vr deste projeto para manter Unity e web sincronizados.
