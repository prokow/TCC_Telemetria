using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.Telemetria.Participants;
using MainProjeto.Telemetria.Supabase;
using MainProjeto.Telemetria.Sessions;
using MainProjeto.Core.Scripts.Model;

internal static class Program
{
    private static int assertions;

    private static async Task Main()
    {
        try { await RunChecks(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static async Task RunChecks()
    {
        CheckConnection();
        CheckSessionLifecycle();
        CheckQuestSequence();
        await CheckAuthentication();
        await CheckRememberedOperatorAuthentication();
        await CheckAuthorizationFromInspector();
        await CheckPagination();
        await CheckInvalidResponses();
        await CheckFailuresAndCancellation();
        await CheckIndependentRosterSync();
        assertions += await TelemetryUploadChecks.Run();
        Console.WriteLine("PASS: " + assertions + " verificações; fontes reais compiladas com bibliotecas do Unity.");
    }

    private static async Task CheckIndependentRosterSync()
    {
        var batches = new Queue<string>(new[] {
            "[" + Row(1, "Mesmo nome") + "]", "[]",
            "[" + Row(1, "Mesmo nome") + "]", "[]",
            "[" + Row(1, "Mesmo nome") + "," + Row(2, "Mesmo nome") + "]", "[]",
            "[" + Row(1, "Nome atualizado") + "," + Row(2, "Mesmo nome") + "]", "[]",
            "[" + Row(2, "Mesmo nome") + "]", "[]"
        });
        var http = new FakeClient((_, _) => Task.FromResult(batches.Dequeue()));
        var sync = new ParticipantRosterSync(new ParticipantRepository(http));
        var first = await sync.RefreshAsync();
        Assert(first.Changed, "primeira consulta publica snapshot");
        Equal(1, first.Participants.Count, "somente registros do Supabase entram na lista");
        var unchanged = await sync.RefreshAsync();
        Assert(!unchanged.Changed, "consulta identica nao exige reconstruir a UI");
        var inserted = await sync.RefreshAsync();
        Assert(inserted.Changed, "novo cadastro no banco atualiza o snapshot");
        Equal(2, inserted.Participants.Count, "homonimos preservados por UUID");
        Assert((await sync.RefreshAsync()).Changed, "renomeacao detectada");
        Equal("Nome atualizado", sync.Snapshot[0].DisplayName, "snapshot mantem nome atualizado");
        Assert((await sync.RefreshAsync()).Changed, "remocao no banco detectada");
        Equal(Code(2), sync.Snapshot[0].ParticipantCode, "snapshot nao mantem cadastro removido");
        Assert(http.Queries.All(query => query.StartsWith("participants?", StringComparison.Ordinal)), "sincronizacao depende apenas do recurso Supabase");

        sync.Clear();
        Equal(0, sync.Snapshot.Count, "troca de conta limpa cache");

        int request = 0;
        var failing = new ParticipantRosterSync(new ParticipantRepository(new FakeClient((_, _) => {
            request++;
            return request == 1 ? Task.FromResult("[" + Row(1, "Pessoa") + "]") : request == 2 ? Task.FromResult("[]")
                : Task.FromException<string>(new SupabaseRequestException(SupabaseFailure.Connection, "Falha simulada"));
        })));
        await failing.RefreshAsync();
        await ThrowsAsync<SupabaseRequestException>(() => failing.RefreshAsync());
        Equal(1, failing.Snapshot.Count, "falha de rede nao equivale a exclusao de participante");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => failing.RefreshAsync(cancellation.Token));
            Equal(1, failing.Snapshot.Count, "cancelamento nao sobrescreve snapshot confirmado");
        }
        var empty = new ParticipantRosterSync(new ParticipantRepository(new FakeClient((_, _) => Task.FromResult("[]"))));
        Assert((await empty.RefreshAsync()).Changed, "primeira lista vazia confirmada");
        Assert(!(await empty.RefreshAsync()).Changed, "lista vazia igual nao oscila");
        empty.Clear();
        Assert((await empty.RefreshAsync()).Changed, "reautenticacao exige primeiro snapshot novo");
    }

    private static void CheckQuestSequence()
    {
        var progress = new QuestCompletionProgress();
        Equal(QuestCompletionResult.None, progress.Complete(0, true, true), "quest vazia não finaliza experiência");
        Equal(QuestCompletionResult.None, progress.Complete(2, false, true), "tarefas pendentes não finalizam experiência");
        for (int quest = 0; quest < 7; quest++)
        {
            progress.LoadQuest();
            Assert(!progress.IsSequenceComplete, "a coleta continua até a última quest");
            Equal(QuestCompletionResult.None, progress.Complete(1, false, quest == 6), "não encerrar durante a narrativa");
            Equal(quest == 6 ? QuestCompletionResult.SequenceCompleted : QuestCompletionResult.QuestCompleted,
                progress.Complete(1, true, quest == 6), "somente a última quest conclui a sequência");
            Equal(QuestCompletionResult.None, progress.Complete(1, true, quest == 6), "conclusão repetida não duplica salvamento");
        }
        Assert(progress.IsSequenceComplete, "última tarefa finaliza experiência");
        progress.LoadQuest();
        Equal(QuestCompletionResult.None, progress.Complete(1, true, true), "recarregar tarefa não reabre experiência finalizada");
        Assert(!new QuestCompletionProgress().IsSequenceComplete, "nova experiência inicia limpa");
    }

    private static string OperatorJwt(string role, string userId, long expiry)
        => "fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            new Newtonsoft.Json.Linq.JObject { ["role"] = role, ["sub"] = userId, ["exp"] = expiry }.ToString()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";

    private static async Task CheckAuthentication()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string userId = Guid.NewGuid().ToString("D");
        string token = OperatorJwt("authenticated", userId, now.AddHours(1).ToUnixTimeSeconds());
        var session = SupabaseAuthSession.FromAccessToken(token, now);
        Equal(userId, session.UserId, "identidade do responsável vem de sub");
        Equal(token, session.AccessToken, "token preservado em memória");
        Assert(!session.IsExpired(now), "token novo válido");
        Assert(session.IsExpired(session.ExpiresAtUtc), "token expira no prazo");
        foreach (string invalid in new[] { "", "invalid", "Bearer " + token, Jwt("authenticated"),
            OperatorJwt("anon", userId, now.AddHours(1).ToUnixTimeSeconds()),
            OperatorJwt("service_role", userId, now.AddHours(1).ToUnixTimeSeconds()),
            OperatorJwt("authenticated", "bad-id", now.AddHours(1).ToUnixTimeSeconds()),
            OperatorJwt("authenticated", userId, now.AddMinutes(-1).ToUnixTimeSeconds()) })
            Throws<SupabaseAuthException>(() => SupabaseAuthSession.FromAccessToken(invalid, now));

        string json = new Newtonsoft.Json.Linq.JObject { ["access_token"] = token, ["refresh_token"] = "unused-fixture" }.ToString();
        var transport = new FakeAuthTransport((_, _) => Task.FromResult(new AuthHttpResponse(200, json)));
        var service = new SupabaseAuthService(transport);
        string password = " Fixture\"\\\nPassword ";
        var authenticated = await service.SignInAsync("  operator@example.invalid  ", password);
        Equal(userId, authenticated.UserId, "login retorna identidade autenticada");
        var sent = Newtonsoft.Json.Linq.JObject.Parse(transport.Bodies[0]);
        Equal("operator@example.invalid", (string)sent["email"], "e-mail sem espaços externos");
        Equal(password, (string)sent["password"], "senha preservada e escapada no JSON");
        Equal(2, sent.Count, "somente e-mail e senha enviados");
        foreach (var credentials in new[] { ("", "password"), ("invalid", "password"), ("user@", "password"), ("operator@example.invalid", "") })
        {
            var inputError = await ThrowsAsync<SupabaseAuthException>(() => service.SignInAsync(credentials.Item1, credentials.Item2));
            Equal(AuthFailure.Input, inputError.Failure, "validar preenchimento antes da rede");
        }
        Equal(1, transport.Bodies.Count, "credenciais inválidas não fazem requisições");
        foreach (var error in new[] {
            (400L, "{\"error_code\":\"invalid_credentials\",\"msg\":\"fixture-secret\"}", AuthFailure.Credentials),
            (400L, "{\"error_code\":\"email_not_confirmed\"}", AuthFailure.EmailNotConfirmed),
            (429L, "rate limited", AuthFailure.RateLimit),
            (401L, "{\"message\":\"fixture-secret\"}", AuthFailure.Configuration),
            (200L, "{}", AuthFailure.InvalidResponse),
            (200L, "[]", AuthFailure.InvalidResponse),
            (200L, "null", AuthFailure.InvalidResponse),
            (200L, "<html>fixture-secret</html>", AuthFailure.InvalidResponse),
            (200L, "{\"access_token\":\"invalid\"}", AuthFailure.InvalidResponse)
        })
        {
            var failureService = new SupabaseAuthService(new FakeAuthTransport((_, _) => Task.FromResult(new AuthHttpResponse(error.Item1, error.Item2))));
            var failure = await ThrowsAsync<SupabaseAuthException>(() => failureService.SignInAsync("operator@example.invalid", "fixture"));
            Equal(error.Item3, failure.Failure, "mapear falha de login");
            Assert(!failure.Message.Contains("fixture-secret"), "não reproduzir corpo de erro");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => service.SignInAsync("operator@example.invalid", "fixture", cancellation.Token));
            Equal(1, transport.Bodies.Count, "cancelamento prévio impede login");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelledService = new SupabaseAuthService(new FakeAuthTransport((_, _) => {
                cancellation.Cancel(); return Task.FromResult(new AuthHttpResponse(200, json));
            }));
            await ThrowsAsync<OperationCanceledException>(() => cancelledService.SignInAsync("operator@example.invalid", "fixture", cancellation.Token));
        }
        var offline = new SupabaseAuthService(new FakeAuthTransport((_, _) => Task.FromException<AuthHttpResponse>(
            new SupabaseAuthException(AuthFailure.Connection, "Sem conexão."))));
        var networkError = await ThrowsAsync<SupabaseAuthException>(() => offline.SignInAsync("operator@example.invalid", "fixture"));
        Equal(AuthFailure.Connection, networkError.Failure, "propagar falha de conexão sem converter em credenciais incorretas");
    }

    private static async Task CheckRememberedOperatorAuthentication()
    {
        string target = WindowsSupabaseSessionStore.BuildTargetName("https://example.supabase.co");
        Equal(target, WindowsSupabaseSessionStore.BuildTargetName("https://EXAMPLE.supabase.co/rest/v1/"), "vault usa escopo canonico do projeto");
        Assert(target != WindowsSupabaseSessionStore.BuildTargetName("https://other.supabase.co"), "projetos nao compartilham credenciais");
        foreach (string invalid in new[] { "http://example.supabase.co", "https://name:pass@example.supabase.co", "https://example.supabase.co/path", "https://example.supabase.co/?token=fixture", "https://example.supabase.co/#fixture" })
            Throws<ArgumentException>(() => WindowsSupabaseSessionStore.BuildTargetName(invalid));

        string access = OperatorJwt("authenticated", Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        string response = new Newtonsoft.Json.Linq.JObject { ["access_token"] = access, ["refresh_token"] = "rotated-fixture" }.ToString();
        var transport = new FakeAuthTransport((_, _) => Task.FromResult(new AuthHttpResponse(200, response)));
        var service = new SupabaseAuthService(transport);
        var store = new FakeSessionStore { Token = "initial-fixture" };
        var login = new RememberedOperatorLogin(service, store);
        var session = await login.RestoreAsync();
        Equal(access, session.AccessToken, "login automatico restaura acesso autenticado");
        Equal("rotated-fixture", store.Token, "refresh rotacionado salvo antes de liberar acesso");
        Equal(1, store.Writes, "uma gravacao por renovacao");
        var sent = Newtonsoft.Json.Linq.JObject.Parse(transport.Bodies[0]);
        Equal(1, sent.Count, "renovacao nao envia email ou senha");
        Equal("initial-fixture", (string)sent["refresh_token"], "usa autorizacao local");
        await login.RestoreAsync();
        Equal("rotated-fixture", (string)Newtonsoft.Json.Linq.JObject.Parse(transport.Bodies[1])["refresh_token"], "proxima renovacao usa token rotacionado");

        int previousRequests = transport.Bodies.Count;
        var missing = new FakeSessionStore();
        await ThrowsAsync<OperatorAuthorizationMissingException>(() => new RememberedOperatorLogin(service, missing).RestoreAsync());
        Equal(previousRequests, transport.Bodies.Count, "sem autorizacao local nao faz login anonimo");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => login.RestoreAsync(cancellation.Token));
            Equal(previousRequests, transport.Bodies.Count, "cancelamento previo impede renovacao");
        }
        var failingSave = new FakeSessionStore { Token = "initial-fixture", FailSave = true };
        await ThrowsAsync<InvalidOperationException>(() => new RememberedOperatorLogin(service, failingSave).RestoreAsync());
        Equal("initial-fixture", failingSave.Token, "falha no cofre nao libera sessao sem persistencia");

        foreach (var failure in new[] {
            (400L, "{\"error_code\":\"refresh_token_not_found\"}", AuthFailure.Credentials),
            (401L, "{}", AuthFailure.Credentials),
            (429L, "{}", AuthFailure.RateLimit),
            (200L, "{\"access_token\":\"" + access + "\"}", AuthFailure.InvalidResponse),
            (200L, "{}", AuthFailure.InvalidResponse),
            (200L, "<html>fixture-secret</html>", AuthFailure.InvalidResponse)
        })
        {
            var errorService = new SupabaseAuthService(new FakeAuthTransport((_, _) => Task.FromResult(new AuthHttpResponse(failure.Item1, failure.Item2))));
            var errorStore = new FakeSessionStore { Token = "remembered-fixture" };
            var error = await ThrowsAsync<SupabaseAuthException>(() => new RememberedOperatorLogin(errorService, errorStore).RestoreAsync());
            Equal(failure.Item3, error.Failure, "renovacao trata falhas e respostas invalidas");
            Equal("remembered-fixture", errorStore.Token, "erro nao sobrescreve autorizacao local");
            Equal(0, errorStore.Writes, "erro nao grava token incompleto");
            Assert(!error.Message.Contains("fixture-secret"), "mensagem de renovacao nao expoe resposta bruta");
        }
        var offline = new SupabaseAuthService(new FakeAuthTransport((_, _) => Task.FromException<AuthHttpResponse>(
            new SupabaseAuthException(AuthFailure.Connection, "Sem conexao."))));
        var offlineStore = new FakeSessionStore { Token = "remembered-fixture" };
        var offlineError = await ThrowsAsync<SupabaseAuthException>(() => new RememberedOperatorLogin(offline, offlineStore).RestoreAsync());
        Equal(AuthFailure.Connection, offlineError.Failure, "offline nao significa acesso revogado");
        Equal("remembered-fixture", offlineStore.Token, "offline preserva autorizacao para reconectar");
    }

    private static async Task CheckAuthorizationFromInspector()
    {
        string access = OperatorJwt("authenticated", Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        string json = new Newtonsoft.Json.Linq.JObject { ["access_token"] = access, ["refresh_token"] = "inspector-refresh-fixture" }.ToString();
        var transport = new FakeAuthTransport((_, _) => Task.FromResult(new AuthHttpResponse(200, json)));
        var store = new FakeSessionStore();
        var login = new RememberedOperatorLogin(new SupabaseAuthService(transport), store);
        var session = await login.AuthorizeAsync("  responsible@example.invalid  ", "Fixture-password");
        Equal(access, session.AccessToken, "autorizacao inicial retorna acesso para abrir lista");
        Equal("inspector-refresh-fixture", store.Token, "autorizacao pelo Inspector guarda somente token renovavel");
        Equal(1, store.Writes, "login inicial grava autorizacao antes de retornar sucesso");
        var sent = Newtonsoft.Json.Linq.JObject.Parse(transport.Bodies[0]);
        Equal("responsible@example.invalid", (string)sent["email"], "Inspector usa login de Authentication existente");
        Equal("Fixture-password", (string)sent["password"], "senha temporaria enviada ao servico de login");
        await login.RestoreAsync();
        Equal("inspector-refresh-fixture", (string)Newtonsoft.Json.Linq.JObject.Parse(transport.Bodies[1])["refresh_token"], "proximo acesso restaura sem pedir senha");

        foreach (var failure in new[] {
            (400L, "{\"error_code\":\"invalid_credentials\"}", AuthFailure.Credentials),
            (200L, "{\"access_token\":\"" + access + "\"}", AuthFailure.InvalidResponse)
        })
        {
            var badAuth = new SupabaseAuthService(new FakeAuthTransport((_, _) => Task.FromResult(new AuthHttpResponse(failure.Item1, failure.Item2))));
            var existing = new FakeSessionStore { Token = "previous-authorization-fixture" };
            var error = await ThrowsAsync<SupabaseAuthException>(() => new RememberedOperatorLogin(badAuth, existing).AuthorizeAsync("responsible@example.invalid", "Fixture-password"));
            Equal(failure.Item3, error.Failure, "login inicial rejeita credenciais invalidas ou sessao nao renovavel");
            Equal(0, existing.Writes, "falha no login inicial nao altera o cofre");
            Equal("previous-authorization-fixture", existing.Token, "falha preserva autorizacao anterior");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            int before = transport.Bodies.Count;
            await ThrowsAsync<OperationCanceledException>(() => login.AuthorizeAsync("responsible@example.invalid", "Fixture-password", cancellation.Token));
            Equal(before, transport.Bodies.Count, "cancelamento previo impede autorizacao pelo Inspector");
        }
        var failingStore = new FakeSessionStore { FailSave = true };
        await ThrowsAsync<InvalidOperationException>(() => new RememberedOperatorLogin(new SupabaseAuthService(transport), failingStore)
            .AuthorizeAsync("responsible@example.invalid", "Fixture-password"));
        Equal(0, failingStore.Writes, "falha na persistencia nao publica autorizacao inicial");
    }

    private sealed class FakeSessionStore : ISupabaseSessionStore
    {
        public string Token;
        public int Writes;
        public bool FailSave;
        public string LoadRefreshToken() => Token;
        public void SaveRefreshToken(string token)
        {
            if (FailSave) throw new InvalidOperationException("Fixture: storage unavailable");
            Token = token;
            Writes++;
        }
        public void Clear() => Token = null;
    }

    private sealed class FakeAuthTransport : ISupabaseSessionTransport
    {
        private readonly Func<string, CancellationToken, Task<AuthHttpResponse>> respond;
        public readonly List<string> Bodies = new List<string>();
        public FakeAuthTransport(Func<string, CancellationToken, Task<AuthHttpResponse>> respond) => this.respond = respond;
        public Task<AuthHttpResponse> SignInAsync(string body, CancellationToken token) { Bodies.Add(body); return respond(body, token); }
        public Task<AuthHttpResponse> RefreshAsync(string body, CancellationToken token) { Bodies.Add(body); return respond(body, token); }
    }

    private static void CheckSessionLifecycle()
    {
        var session = new SessionContext();
        var participant = new Participant(Code(1), "Pessoa");
        var other = new Participant(Code(2), "Pessoa");
        Equal(LocalSessionState.None, session.State, "sessão inicial vazia");
        Assert(!session.Prepare(null), "sessão exige participante");
        Assert(!session.MarkActive("invalid"), "sessão não preparada não pode iniciar");
        Assert(session.Prepare(participant), "preparar sessão");
        string id = session.SessionId;
        Assert(Guid.TryParseExact(id, "N", out _), "sessão usa GUID N");
        Equal(participant.ParticipantCode, session.ParticipantCode, "sessão vinculada à PK real");
        Assert(session.Prepare(participant), "preparação repetida aceita");
        Equal(id, session.SessionId, "clique repetido mantém UUID");
        Assert(!session.MarkActive(Code(99)), "não ativar outro UUID");
        Assert(session.MarkActive(id), "ativar sessão preparada");
        Assert(!session.MarkActive(id), "não ativar duas vezes");
        Assert(!session.Prepare(other), "não trocar de pessoa durante sessão ativa");
        Assert(!session.DiscardPreparation(), "não apagar sessão ativa");
        Equal(id, session.SessionId, "bloqueios mantêm identidade");
        Assert(!session.MarkSavedLocally(id), "salvar exige encerramento");
        Assert(!session.MarkPendingLocalSave(Code(99)), "não encerrar UUID incorreto");
        Assert(session.MarkPendingLocalSave(id), "encerramento aguarda persistência local");
        Assert(!session.CanChangeParticipant, "salvamento pendente bloqueia troca");
        Assert(!session.Prepare(other), "não substituir sessão pendente");
        Assert(!session.DiscardPreparation(), "não apagar sessão pendente");
        Assert(session.MarkSavedLocally(id), "confirmar somente salvamento local");
        Assert(session.CanChangeParticipant, "salvamento libera próxima pessoa");
        Assert(session.Prepare(other), "preparar próxima sessão");
        Assert(session.SessionId != id, "nova execução recebe UUID novo");
        Assert(session.DiscardPreparation(), "permitir invalidar seleção preparada");
        Equal(null, session.SessionId, "descartar preparação limpa UUID");

        var manager = new ParticipantManager();
        int changes = 0;
        manager.Changed += () => changes++;
        manager.Select(participant);
        Equal(participant, manager.Selected, "seleção mantém participante real");
        manager.Select(other);
        Equal(other.ParticipantCode, manager.Selected.ParticipantCode, "homônimos selecionados pela PK");
        manager.Clear();
        Equal(null, manager.Selected, "limpar seleção");
        Equal(3, changes, "notificações de seleção");
        Throws<ArgumentNullException>(() => manager.Select(null));
    }

    private static void CheckConnection()
    {
        foreach (string suffix in new[] { "", "/", "/rest/v1", "/rest/v1/" })
        {
            var connection = new SupabaseConnection("https://example.invalid" + suffix, "sb_publishable_test_fixture");
            Equal("https://example.invalid/rest/v1/", connection.RestUrl, "URL REST normalizada");
            Equal(null, connection.GetAuthorizationHeader(null), "publishable não é Bearer");
        }
        string anon = Jwt("anon");
        var legacy = new SupabaseConnection("https://example.invalid", anon);
        Equal("Bearer " + anon, legacy.GetAuthorizationHeader(null), "anon JWT compatível");
        string authenticated = Jwt("authenticated");
        Equal("Bearer " + authenticated, legacy.GetAuthorizationHeader(authenticated), "JWT do operador tem prioridade");
        foreach (string invalid in new[] { "sb_secret_fixture", Jwt("service_role"), Jwt("authenticated"), "invalid", "" })
            Throws<ArgumentException>(() => new SupabaseConnection("https://example.invalid", invalid));
        foreach (string invalid in new[] { "http://example.invalid", "https://user:password@example.invalid", "https://example.invalid/path", "https://example.invalid?key=x", "https://example.invalid#x", "invalid" })
            Throws<ArgumentException>(() => new SupabaseConnection(invalid, anon));
        foreach (string invalid in new[] { Jwt("service_role"), anon, "sb_secret_fixture", "Bearer " + authenticated })
            Throws<ArgumentException>(() => legacy.GetAuthorizationHeader(invalid));
        Throws<ArgumentOutOfRangeException>(() => new SupabaseConnection("https://example.invalid", anon, 0));
        Throws<ArgumentOutOfRangeException>(() => new SupabaseConnection("https://example.invalid", anon, 121));
        var participant = new Participant("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", null);
        Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", participant.ParticipantCode, "preservar PK textual");
        Equal("Participante AAAAAAAA", participant.DisplayName, "nome nulo tem fallback visual");
        Throws<ArgumentException>(() => new Participant(Guid.NewGuid().ToString("D"), "Nome"));
    }

    private static async Task CheckPagination()
    {
        var empty = new FakeClient((_, _) => Task.FromResult("[]"));
        Equal(0, (await new ParticipantRepository(empty).GetAllAsync()).Count, "lista vazia");

        var pages = new Queue<string>(new[] {
            "[" + Row(1, "Mesmo nome") + "," + Row(2, "Mesmo nome") + "]",
            "[" + Row(3, null) + "]",
            "[]"
        });
        var client = new FakeClient((_, _) => Task.FromResult(pages.Dequeue()));
        var list = await new ParticipantRepository(client, 100).GetAllAsync();
        Equal(3, list.Count, "página curta do servidor não trunca a lista");
        Equal(3, client.Queries.Count, "buscar até página vazia");
        Equal(list[0].DisplayName, list[1].DisplayName, "homônimos mantidos");
        Assert(list[0].ParticipantCode != list[1].ParticipantCode, "UUIDs distintos");
        Equal("Participante 00000000", list[2].DisplayName, "nome nulo permitido pelo SQL");
        Equal("participants?select=participant_code,display_name&order=participant_code.asc&limit=100", client.Queries[0], "somente colunas necessárias");
        Assert(client.Queries[1].EndsWith("&participant_code=gt." + Code(2)), "cursor usa última PK");
        Assert(client.Queries[2].EndsWith("&participant_code=gt." + Code(3)), "cursor progride");

        var manyRows = Enumerable.Range(1, 250).ToArray();
        var many = new FakeClient((query, _) => {
            int cursorIndex = query.IndexOf("&participant_code=gt.", StringComparison.Ordinal);
            int cursor = cursorIndex < 0 ? 0 : Convert.ToInt32(query.Substring(cursorIndex + "&participant_code=gt.".Length), 16);
            string json = "[" + string.Join(",", manyRows.Where(x => x > cursor).Take(37).Select(x => Row(x, "Pessoa"))) + "]";
            return Task.FromResult(json);
        });
        Equal(250, (await new ParticipantRepository(many, 100).GetAllAsync()).Count, "várias páginas sob limite do servidor");
        Equal(8, many.Queries.Count, "sete páginas de dados e página vazia");
    }

    private static async Task CheckInvalidResponses()
    {
        foreach (string json in new[] {
            "", "null", "{}", "<html>error</html>", "[", "[null]", "[{}]",
            "[{\"participant_code\":3,\"display_name\":\"Nome\"}]",
            "[{\"participant_code\":\"bad\",\"display_name\":\"Nome\"}]",
            "[{\"participant_code\":\"" + Code(1) + "\"}]",
            "[{\"participant_code\":\"" + Code(1) + "\",\"display_name\":42}]"
        })
        {
            var client = new FakeClient((_, _) => Task.FromResult(json));
            var ex = await ThrowsAsync<SupabaseRequestException>(() => new ParticipantRepository(client).GetAllAsync());
            Equal(SupabaseFailure.InvalidResponse, ex.Failure, "contrato inválido rejeitado");
            Assert(!ex.Message.Contains("Nome"), "mensagem não reproduz dados da resposta");
        }

        var duplicate = new FakeClient((_, _) => Task.FromResult("[" + Row(1, "A") + "]"));
        var duplicateError = await ThrowsAsync<SupabaseRequestException>(() => new ParticipantRepository(duplicate).GetAllAsync());
        Equal(SupabaseFailure.InvalidResponse, duplicateError.Failure, "cursor repetido não entra em loop");
        Equal(2, duplicate.Queries.Count, "duplicação detectada imediatamente");

        var tooMany = new FakeClient((_, _) => Task.FromResult("[" + Row(1, "A") + "," + Row(2, "B") + "]"));
        await ThrowsAsync<SupabaseRequestException>(() => new ParticipantRepository(tooMany, 100, 1).GetAllAsync());
        Throws<ArgumentOutOfRangeException>(() => new ParticipantRepository(tooMany, 0));
        Throws<ArgumentOutOfRangeException>(() => new ParticipantRepository(tooMany, 1001));
        Throws<ArgumentOutOfRangeException>(() => new ParticipantRepository(tooMany, 100, 0));
    }

    private static async Task CheckFailuresAndCancellation()
    {
        foreach (var failure in new[] { SupabaseFailure.AccessDenied, SupabaseFailure.Connection, SupabaseFailure.Http })
        {
            var expected = new SupabaseRequestException(failure, "Erro simulado", failure == SupabaseFailure.AccessDenied ? 403 : 0);
            int call = 0;
            var client = new FakeClient((_, _) => ++call == 1 ? Task.FromResult("[" + Row(1, "A") + "]") : Task.FromException<string>(expected));
            var actual = await ThrowsAsync<SupabaseRequestException>(() => new ParticipantRepository(client).GetAllAsync());
            Assert(ReferenceEquals(expected, actual), "falha após página inicial é propagada, sem retornar lista parcial");
        }

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var client = new FakeClient((_, _) => Task.FromResult("[]"));
            await ThrowsAsync<OperationCanceledException>(() => new ParticipantRepository(client).GetAllAsync(cancellation.Token));
            Equal(0, client.Queries.Count, "cancelamento prévio impede requisição");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var client = new FakeClient((_, _) => { cancellation.Cancel(); return Task.FromResult("[" + Row(1, "A") + "]"); });
            await ThrowsAsync<OperationCanceledException>(() => new ParticipantRepository(client).GetAllAsync(cancellation.Token));
            Equal(1, client.Queries.Count, "cancelamento após resposta impede uso dos dados e novas páginas");
        }
    }

    private static string Code(int number) => number.ToString("x32");
    private static string Row(int number, string name) => "{\"participant_code\":\"" + Code(number) + "\",\"display_name\":" + (name == null ? "null" : "\"" + name + "\"") + "}";
    private static string Jwt(string role) => "fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"role\":\"" + role + "\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";

    private static void Assert(bool value, string description)
    {
        if (!value) throw new Exception("FAIL: " + description);
        assertions++;
    }
    private static void Equal<T>(T expected, T actual, string description) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), description);
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { assertions++; return; }
        throw new Exception("FAIL: esperado " + typeof(T).Name);
    }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T ex) { assertions++; return ex; }
        throw new Exception("FAIL: esperado " + typeof(T).Name);
    }

    private sealed class FakeClient : ISupabaseReadClient
    {
        private readonly Func<string, CancellationToken, Task<string>> response;
        public readonly List<string> Queries = new List<string>();
        public FakeClient(Func<string, CancellationToken, Task<string>> response) => this.response = response;
        public Task<string> GetAsync(string query, CancellationToken token) { Queries.Add(query); return response(query, token); }
    }
}
