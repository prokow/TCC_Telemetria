# Primeiro login do responsável no Unity

> O fluxo padrão da MainMenu agora usa acesso automático no Windows. Siga [LoginAutomaticoParticipantes.md](LoginAutomaticoParticipantes.md). As instruções de login manual abaixo descrevem o fluxo anterior, mantido como referência.

O usuário de Authentication e o UUID autorizado na política já foram definidos pelo responsável pelo projeto. Esta entrega acrescenta o login à MainMenu existente; nenhuma alteração foi executada no Supabase remoto e nenhuma senha foi solicitada ou gravada em arquivo.

## O que fazer agora

1. Confirme que executou o SQL da política no **SQL Editor** e recebeu sucesso. Substituir o UUID em um texto sem executar a consulta não altera as permissões.
2. Confirme em **Authentication > Users** que o usuário pertence ao mesmo projeto da URL configurada no Unity e que o e-mail está confirmado, se essa confirmação for exigida.
3. Abra ou recarregue `Assets/Scenes/MainMenu.unity` no Unity, preservando eventuais edições próprias ainda não salvas. Aguarde a compilação dos scripts.
4. Entre em **Play Mode**. Clique em **Entrar como responsável**, digite o e-mail e a senha dessa conta e clique em **Entrar**. No Editor, use mouse e teclado para preencher os campos.
5. Depois do login, o painel de participantes abre e a lista é consultada com o token do responsável. Se houver alguém em `public.participants`, selecione a pessoa, volte ao menu e inicie o simulador.

Não cole senha, JWT ou chave administrativa no Inspector. O asset MenuSupabaseConnection continua armazenando somente URL, chave pública e timeout. O login não cria contas novas; utiliza o usuário já criado em Authentication.

## Se a lista ficar vazia

Uma conta em Authentication é a identidade de quem opera o simulador. As pessoas que aparecerão na lista vêm de **Table Editor > public.participants**. Esses cadastros são independentes.

Para um primeiro teste, você pode inserir um registro fictício pelo SQL Editor:

```sql
insert into public.participants (participant_code, display_name)
values (
    '0d850f8440dc4d9eb934f9d081e9b6e9',
    'Participante de teste'
)
on conflict (participant_code) do nothing;
```

Esse código tem 32 caracteres, sem hífens, conforme o contrato usado pelo Unity. O insert usa um identificador fixo para que repetir o teste não crie vários registros. Depois, clique em **Atualizar** no painel de participantes. É possível remover esse registro fictício pelo Table Editor após o teste.

Se a tabela já tem registros e a conta recebe uma lista vazia, conferir a política: a comparação deve usar o UUID do usuário de **Authentication**, e não participant_code. Uma política que filtra todas as linhas também pode retornar lista vazia. O sucesso de uma consulta no SQL Editor não comprova que o usuário do Unity tem permissão pela API.

## Mensagens e verificações

| Mensagem ou situação | O que verificar |
| --- | --- |
| E-mail ou senha incorretos | Use as credenciais do usuário em Authentication do projeto conectado |
| Confirme seu e-mail | Confirmação do usuário em Authentication |
| Acesso à lista não autorizado | SQL executado, GRANT nas duas colunas e política SELECT para authenticated |
| Login funciona, mas lista vazia | Cadastro em participants e UUID da conta autorizado pela política |
| Sem conexão | Internet, Project URL e disponibilidade do projeto Supabase |
| Muitas tentativas | Aguarde antes de tentar entrar novamente |
| Seu acesso expirou | Entre novamente; este incremento não mantém refresh token |

O menu possui **Trocar conta**, que cancela consultas, remove a lista anterior e descarta o token em memória antes de outro login. A troca permanece bloqueada se a sessão de telemetria estiver ativa ou tiver salvamento pendente. O início exige login válido e participante presente na consulta validada.

## O que foi implementado

- [SupabaseAuthService.cs](../Assets/MainProjeto/Telemetria/Supabase/SupabaseAuthService.cs): valida o preenchimento, envia e-mail/senha para `/auth/v1/token?grant_type=password` e trata erros sem reproduzir o corpo da resposta.
- [SupabaseAuthSession.cs](../Assets/MainProjeto/Telemetria/Supabase/SupabaseAuthSession.cs): mantém token, identidade do responsável e expiração somente em memória. A leitura local dos campos do JWT não substitui a verificação de assinatura e autorização feita pelo Supabase.
- [OperatorLoginUI.cs](../Assets/MainProjeto/MenuPrincipal/Scripts/OperatorLoginUI.cs): campos TMP, senha mascarada, estados de envio, cancelamento e retorno. A senha é retirada do campo antes de aguardar a resposta.
- [MenuIntegrationController.cs](../Assets/MainProjeto/MenuPrincipal/Scripts/MenuIntegrationController.cs): abre o login, usa o token nas consultas, invalida a lista ao trocar de conta e exige nova entrada após expiração.
- `OperatorLoginPanel`: painel já adicionado ao Canvas World Space existente, com todas as referências do Inspector preenchidas. Mantém o XR Origin e os controles atuais.

O botão de entrada reutiliza ButtonParticipantes: mostra **Entrar como responsável** antes de autenticar e **Selecionar participante** depois. Não foi adicionada outra cena de menu.

Não há renovação automática do token, persistência de credenciais ou restauração do login após fechar o aplicativo/voltar de outra cena. Para esta etapa, a expiração encerra o acesso local e solicita novo login. Trocar conta descarta o acesso local, sem encerrar sessões em outros dispositivos no Supabase.

Os campos usam o suporte de teclado do TMP existente. O login no Editor pode ser testado por mouse/teclado. O teclado virtual e o preenchimento usando apenas os controles VR ainda exigem validação no headset de destino; não foi adicionado um teclado VR próprio.

## Validação desta entrega

Passaram **179 verificações automatizadas**, incluindo consulta e paginação anteriores, validação do token de operador, expiração, dados de login, escape de caracteres de senha, erros de credenciais/e-mail não confirmado, limite de tentativas, respostas inválidas, cancelamento e falhas de conexão.

Os arquivos foram compilados contra as bibliotecas locais do Unity 6000.3.14f1. O YAML, as referências e os campos vazios/mascarados foram validados. Não houve login real, execução de SQL remoto ou teste em Play Mode/headset: o primeiro teste usa a conta que você criou, digitada por você no aplicativo.

As sessões do simulador e a telemetria continuam locais. O sucesso do login e da leitura de participantes não significa que já há gravação em public.sessions ou upload de arquivos. Essa será uma integração posterior com suas permissões específicas.

Referências: [Supabase Auth: senha](https://supabase.com/docs/guides/auth/passwords), [API de autenticação](https://supabase.github.io/auth/), [sessões e tokens](https://supabase.com/docs/guides/auth/sessions).
