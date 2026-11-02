# Etapa 4 — conexão e consulta de participantes

Este documento descreve a entrega inicial da etapa 4, limitada à leitura de participantes. A integração posterior na cena MainMenu, com seleção e sessão local, está descrita em [MainMenuParticipantesConfigurado.md](MainMenuParticipantesConfigurado.md). A escrita remota e as políticas do banco continuam fora desses incrementos.

## Contrato confirmado

| Origem SQL | Modelo Unity |
| --- | --- |
| public.participants.participant_code, text, PK | Participant.ParticipantCode: GUID de 32 caracteres no formato N |
| public.participants.display_name, text, nullable | Participant.DisplayName: nome ou indicação visual com parte do código |

A identidade é sempre participant_code. Nomes repetidos são aceitos. O SQL declara esse identificador como text e apenas comenta o formato N; a aplicação valida o formato para impedir associações com identidades inválidas. O frontend deverá produzir um GUID N, como o gravador atual, ou será necessário definir uma conversão consistente entre frontend e banco. O código preserva o texto da chave, inclusive maiúsculas, porque a PK não tem tipo uuid.

Não há coluna de disponibilidade; o repositório consulta todos os participantes visíveis à identidade autorizada. Não há coluna de estado em sessions: o estado de execução proposto ficará no contexto local, salvo futura migração explicitamente definida.

## Scripts completos disponíveis no projeto

| Arquivo | Responsabilidade |
| --- | --- |
| [SupabaseConnection.cs](../Assets/MainProjeto/Telemetria/Supabase/SupabaseConnection.cs) | Valida URL HTTPS, normaliza /rest/v1, aceita somente chave pública e separa JWT do responsável |
| [SupabaseConnectionSettings.cs](../Assets/MainProjeto/Telemetria/Supabase/SupabaseConnectionSettings.cs) | Asset configurável no Inspector; não armazena login ou token do responsável |
| [ISupabaseReadClient.cs](../Assets/MainProjeto/Telemetria/Supabase/ISupabaseReadClient.cs) | Contrato de consulta e exceção tipada para falha de conexão, acesso, HTTP ou resposta |
| [SupabaseService.cs](../Assets/MainProjeto/Telemetria/Supabase/SupabaseService.cs) | GET assíncrono via UnityWebRequest, timeout e cancelamento na thread do Unity |
| [Participant.cs](../Assets/MainProjeto/Telemetria/Participants/Participant.cs) | Identidade e nome, sem dependência de Canvas ou MonoBehaviour |
| [ParticipantRepository.cs](../Assets/MainProjeto/Telemetria/Participants/ParticipantRepository.cs) | Consulta, paginação e validação das respostas, sem dependência de cena |
| [ParticipantQueryProbe.cs](../Assets/MainProjeto/Telemetria/Participants/ParticipantQueryProbe.cs) | Verificação manual em Play Mode, sem alterar o menu ou imprimir nomes |

O projeto já possui o pacote com.unity.nuget.newtonsoft-json 3.2.2 instalado. Esta implementação utiliza esse pacote para validar a estrutura e os tipos do JSON. Não foi instalada outra biblioteca.

## Configuração no Inspector

1. Aguarde o Unity importar os novos scripts e verifique o Console.
2. Selecione o asset `Assets/MainProjeto/Telemetria/Supabase/Settings/MenuSupabaseConnection.asset`. Ele já contém a Project URL e a chave pública reaproveitadas de SupabaseManager, com a URL normalizada, e timeout de 20 segundos.
3. Para outro projeto, crie um asset por **Create > MainProjeto > Telemetria > Conexão Supabase**. Configure Project Url, Public Api Key e Timeout Seconds. Não use service_role, sb_secret ou token do responsável no asset.
4. Para verificar sem salvar alterações na cena: abra MainMenu, entre em Play Mode, crie um GameObject temporário e adicione ParticipantQueryProbe.
5. Arraste MenuSupabaseConnection para o campo Settings do componente.
6. No menu de contexto do componente, execute **Consultar participantes (Play Mode)**. Não há requisição automática em Awake ou Start.
7. Confira o Console: ele informa a quantidade recebida, lista sem registros visíveis ou falha de acesso/conexão. A consulta não lista nomes nem códigos no Console.
8. Sair de Play Mode remove o GameObject temporário. Nenhum componente precisa ser anexado permanentemente nesta etapa.

O componente ignora uma segunda consulta enquanto a primeira estiver em execução. Ao desativar o componente, ele cancela a consulta e elimina o JWT da memória do componente. Os handlers de erro evitam reproduzir dados pessoais ou credenciais.

## O bloqueio de acesso do SQL é esperado

O script fornecido habilita RLS e revoga privilégios em participants para anon e authenticated. Não cria uma política de SELECT. Portanto, reutilizar a chave pública, ou apenas acrescentar um login, não concede acesso à tabela.

A nova consulta usa:

```text
GET /rest/v1/participants
    ?select=participant_code,display_name
    &order=participant_code.asc
    &limit=100
```

As páginas seguintes acrescentam `participant_code=gt.<ultimo_codigo>`. A consulta continua até uma página vazia, mesmo quando o servidor retorna menos linhas que o solicitado. Não retorna uma lista parcial se uma página falhar. Para impedir uma consulta sem limite, existe um teto configurável de 10.000 participantes; excedê-lo produz erro explícito. A paginação não representa um snapshot transacional: cadastros inseridos antes do cursor durante a consulta podem aparecer somente na próxima atualização.

Com o SQL atual, uma resposta 401/403 deve ser tratada como **acesso negado**, com orientação para autenticação, GRANT e RLS. Se uma configuração futura permitir SELECT mas uma política filtrar todas as linhas, uma resposta `[]` significa somente que não há registros visíveis à identidade atual; o cliente não pode concluir que a tabela inteira esteja vazia.

Antes de liberar acesso no banco, é necessário definir se o responsável fará login com uma permissão específica de operador ou se a leitura passará por um backend autorizado. O código não cria `USING (true)`, não concede acesso a todo usuário autenticado e não altera o banco remoto. A definição de operador não consta no SQL recebido.

Quando houver autenticação no Unity, o fluxo fornecerá o access_token em memória:

```csharp
var service = new SupabaseService(settings.CreateConnection(), () => tokenAtualDoResponsavel);
var repository = new ParticipantRepository(service);
var participants = await repository.GetAllAsync(cancellationToken);
```

Esse exemplo é um ponto de integração; tokenAtualDoResponsavel será propriedade do futuro fluxo de autenticação, que também cuidará da renovação. O probe oferece SetAccessToken para esse mesmo propósito, sem campo serializado. O login web não é transferido automaticamente para o Unity.

O serviço envia a chave pública em apikey. Uma chave publishable não é enviada como Bearer. Com uma chave anon legada e sem login, usa o JWT anon como Bearer; com login, usa o JWT authenticated do responsável. A verificação local do campo role rejeita chaves administrativas por engano, mas não substitui a verificação de assinatura, expiração e autorização do Supabase.

## Verificação realizada

Runner reproduzível: [Tools/ParticipantIntegrationChecks/Run.ps1](../Tools/ParticipantIntegrationChecks/Run.ps1).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ParticipantIntegrationChecks/Run.ps1
```

O runner usa o SDK .NET 9 instalado, as bibliotecas reais do Unity 6000.3.14f1 e o Newtonsoft já instalado pelo Unity. Compila os sete arquivos novos e executa verificações do modelo, configuração e repositório com transporte simulado, sem chamadas de rede. Os arquivos gerados ficam em Temp/ParticipantIntegrationChecks.

Resultado: **91 verificações passaram**. Foram verificados normalização da URL, separação de chaves e JWT, recusa de credenciais administrativas, lista vazia, homônimos, nomes nulos, paginação com limite menor imposto pelo servidor, várias páginas, payloads inválidos, repetição de códigos, limite de resultados, falha em páginas posteriores e cancelamento antes/depois da resposta.

Não foi executado Play Mode, teste em headset ou integração com o banco real. Compilar contra as bibliotecas do Unity não testa o transporte nativo UnityWebRequest em execução. A consulta real depende da autorização ainda ausente no SQL; este incremento não altera essa restrição.

## Próximo incremento

O painel dinâmico, validação de início e persistência local já foram conectados ao repositório na entrega descrita em [MainMenuParticipantesConfigurado.md](MainMenuParticipantesConfigurado.md). A consulta real depende de autorização; criação de sessão no banco e upload continuam pendentes dessa definição. O SupabaseManager legado permanece intacto; a nova conexão já evita a duplicação de /rest/v1, mas o envio antigo ainda precisa de sua adaptação planejada.

Referências: [Supabase: API keys](https://supabase.com/docs/guides/getting-started/api-keys), [Supabase: Row Level Security](https://supabase.com/docs/guides/database/postgres/row-level-security), [PostgREST: paginação](https://docs.postgrest.org/en/stable/references/api/pagination_count.html).
