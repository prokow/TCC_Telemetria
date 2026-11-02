using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.Telemetria;
using MainProjeto.Telemetria.Supabase;
using MainProjeto.Telemetria.Upload;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class TelemetryUploadChecks
{
    private static int checks;
    private static readonly string Owner = "00000000-0000-0000-0000-000000000001";
    private static readonly string Participant = "00000000000000000000000000000002";
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL Upload: " + label); checks++; }
    private static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { checks++; return; } throw new Exception("Missing expected " + typeof(T).Name); }
    public static async Task<int> Run()
    {
        var setup = Fixture(); var package = TelemetryUploadPackage.Build(setup.store.Folder(setup.payload.SessionId), Owner);
        Check(package.Files.Count == 4, "quatro arquivos canonicos");
        Check(package.Files.All(file => file.StoragePath.StartsWith(Owner + "/" + Participant + "/" + package.SessionId + "/")), "todos os caminhos vinculam operador, participante e sessao");
        Check(package.Files.All(file => file.Sha256 == TelemetryUploadPackage.Hash(file.Bytes)), "SHA de cada arquivo");
        Check(package.Files.Where(file => file.Name.EndsWith(".csv")).All(file => Encoding.UTF8.GetString(file.Bytes).StartsWith("ParticipantCode,SessionId,")), "UUIDs em todos os CSVs");
        var headValues = Encoding.UTF8.GetString(package.Files.Single(file => file.Name == "Head.csv").Bytes).Split('\n')[1].Split(',');
        var controllerValues = Encoding.UTF8.GetString(package.Files.Single(file => file.Name == "Controllers.csv").Bytes).Split('\n')[1].Split(',');
        Check(headValues.Length == 9 && headValues[3] == "-1" && headValues[7] == "70", "posicao e rotacao reais do headset presentes no Head.csv");
        Check(controllerValues.Length == 21 && controllerValues[4] == "2.5" && controllerValues[15] == "-3" && controllerValues[20] == "0.75", "posicao de cada controle e analogico presentes no Controllers.csv");
        Check(Encoding.UTF8.GetString(package.Files.Single(file => file.Name == "Events.csv").Bytes).Contains("'=fixture"), "formulas neutralizadas apenas em campos de texto");
        Check((int)package.Summary["sample_counts"]["left_tracked"] == 1 && (int)package.Summary["sample_counts"]["right_tracked"] == 0, "contagens reais de rastreio");
        Check(package.Digest == TelemetryUploadPackage.Build(setup.store.Folder(setup.payload.SessionId), Owner).Digest, "reenvio gera o mesmo digest");
        await Throws<TelemetryUploadException>(() => Task.Run(() => TelemetryUploadPackage.Build(setup.store.Folder(setup.payload.SessionId), "invalid")));
        var bad = Fixture(); bad.payload.ParticipantCode = "same-name-is-not-uuid"; Save(bad.store, bad.payload);
        await Throws<TelemetryUploadException>(() => Task.Run(() => TelemetryUploadPackage.Build(bad.store.Folder(bad.payload.SessionId), Owner)));

        var api = new Api(); var auth = new Auth(); var clock = DateTimeOffset.UtcNow;
        var processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid", () => clock);
        await processor.ProcessAsync(default);
        Check(setup.store.Read(package.SessionId).Status == "Uploaded", "somente ACK completo marca enviado");
        Check(api.Objects.Count == 4 && api.Sessions.Count == 1 && api.EventCount == 2, "arquivos, sessao e eventos presentes");
        Check(api.Tokens.All(token => token == auth.Session.AccessToken), "JWT do responsavel em todas as chamadas");
        int calls = api.Calls; await processor.ProcessAsync(default); Check(api.Calls == calls, "sessao confirmada nao e enviada novamente");

        setup = Fixture(); api = new Api { FailFileOnce = "Head.csv" }; clock = DateTimeOffset.UtcNow;
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid", () => clock);
        await processor.ProcessAsync(default);
        var state = setup.store.Read(setup.payload.SessionId);
        Check(state.Status == "Pending" && state.ConfirmedFiles == 2 && api.Sessions.Count == 0, "falha parcial fica pendente sem resumo incompleto");
        calls = api.Calls; await processor.ProcessAsync(default); Check(api.Calls == calls, "backoff evita repetir imediatamente");
        clock = clock.AddMinutes(10);
        var restored = new TelemetryUploadProcessor(new TelemetryUploadStore(setup.store.Root), auth, new TelemetryUploadClient(api), "https://example.invalid", () => clock);
        await restored.ProcessAsync(default);
        Check(setup.store.Read(setup.payload.SessionId).Status == "Uploaded", "reinicio retoma a fila persistida");
        Check(api.Objects.Count == 4 && api.Sessions.Count == 1 && api.EventCount == 2 && api.VerifiedDuplicates == 2, "retomada confere hashes e nao duplica dados");
        Check(File.Exists(Path.Combine(setup.store.Folder(setup.payload.SessionId), "Session.json")), "backup local continua disponivel");

        setup = Fixture(); api = new Api { LoseFirstAck = true }; clock = DateTimeOffset.UtcNow;
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid", () => clock);
        await processor.ProcessAsync(default);
        Check(setup.store.Read(setup.payload.SessionId).Status == "Pending" && api.Sessions.Count == 1, "timeout apos commit nao anuncia sucesso local; " + setup.store.Read(setup.payload.SessionId).LastErrorType + "; commits=" + api.Sessions.Count);
        clock = clock.AddMinutes(10); await processor.ProcessAsync(default);
        Check(setup.store.Read(setup.payload.SessionId).Status == "Uploaded" && api.EventCount == 2, "ACK perdido e recuperado sem duplicar eventos");

        setup = Fixture(); api = new Api { MissingMigration = true }; clock = DateTimeOffset.UtcNow;
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid", () => clock);
        await processor.ProcessAsync(default); state = setup.store.Read(setup.payload.SessionId);
        Check(state.Status == "Pending" && state.LastError.Contains("007"), "permissao ausente mantem pendencia e orienta migracao");
        api.MissingMigration = false; clock = clock.AddMinutes(10); await processor.ProcessAsync(default);
        Check(setup.store.Read(setup.payload.SessionId).Status == "Uploaded", "aplicar migracao permite reenvio posterior");

        setup = Fixture(); api = new Api { AuthorizedParticipant = false };
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid");
        await processor.ProcessAsync(default);
        Check(api.Objects.Count == 0 && setup.store.Read(setup.payload.SessionId).Status == "ReviewRequired", "UUID nao cadastrado nao cria objetos nem participantes fantasmas");

        setup = Fixture(); package = TelemetryUploadPackage.Build(setup.store.Folder(setup.payload.SessionId), Owner); api = new Api();
        api.Objects["/storage/v1/object/vr-telemetry/" + package.Files[0].StoragePath] = Encoding.UTF8.GetBytes("other-content");
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid"); await processor.ProcessAsync(default);
        Check(setup.store.Read(package.SessionId).Status == "ReviewRequired" && api.Sessions.Count == 0, "conflito de conteudo nao sobrescreve arquivo remoto");

        setup = Fixture(); api = new Api { FirstUnauthorized = true };
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid"); await processor.ProcessAsync(default);
        Check(auth.Invalidations == 1 && setup.store.Read(setup.payload.SessionId).Status == "Pending", "401 invalida token sem perder dados");

        setup = Fixture(); api = new Api { BlockCommit = new TaskCompletionSource<TelemetryHttpResponse>() };
        processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid");
        var pending = processor.ProcessAsync(default);
        while (!api.CommitReached) await Task.Delay(1);
        Check(setup.store.Read(setup.payload.SessionId).Status == "Uploading", "quatro uploads sem ACK nao bastam para marcar sucesso");
        calls = api.Calls; await processor.ProcessAsync(default); Check(api.Calls == calls, "cliques concorrentes nao iniciam segundo envio");
        api.BlockCommit.SetResult(api.LastAck); await pending;
        Check(setup.store.Read(setup.payload.SessionId).Status == "Uploaded", "ACK libera conclusao");

        var foreign = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://other.invalid");
        calls = api.Calls; await foreign.ProcessAsync(default, true); Check(api.Calls == calls, "fila nao e enviada para outro projeto");
        setup = Fixture(); var bound = TelemetryUploadPackage.Build(setup.store.Folder(setup.payload.SessionId), Owner);
        setup.store.Save(new TelemetryUploadState { ProjectOrigin = "https://example.invalid", OperatorId = "00000000-0000-0000-0000-000000000099", ParticipantCode = Participant, SessionId = bound.SessionId, Digest = bound.Digest });
        api = new Api(); processor = new TelemetryUploadProcessor(setup.store, auth, new TelemetryUploadClient(api), "https://example.invalid"); await processor.ProcessAsync(default, true);
        Check(api.Calls == 0, "trocar operador nao envia fila pertencente a outro UUID de Authentication");
        using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); await Throws<OperationCanceledException>(() => processor.ProcessAsync(cancellation.Token)); }

        setup = Fixture(); setup.payload.CollectorUserId = "00000000-0000-0000-0000-000000000099"; Save(setup.store,setup.payload);
        api=new Api(); processor=new TelemetryUploadProcessor(setup.store,auth,new TelemetryUploadClient(api),"https://example.invalid"); await processor.ProcessAsync(default);
        Check(api.Calls==0, "UUID do coletor original impede vincular coleta a outro login");
        setup = Fixture(); setup.payload.SupabaseProjectOrigin="https://other.invalid"; Save(setup.store,setup.payload);
        api=new Api(); processor=new TelemetryUploadProcessor(setup.store,auth,new TelemetryUploadClient(api),"https://example.invalid"); await processor.ProcessAsync(default);
        Check(api.Calls==0, "origem gravada na coleta impede envio a outro banco");
        var sessionTransport = new RefreshTransport(auth.Session.AccessToken);
        var shared = new SharedSupabaseSession(new RememberedOperatorLogin(new SupabaseAuthService(sessionTransport),new MemorySessionStore()));
        var simultaneous = await Task.WhenAll(Enumerable.Range(0,10).Select(_=>shared.GetAsync(default)));
        Check(sessionTransport.Refreshes==1 && simultaneous.All(item=>item.UserId==Owner), "menu e uploader compartilham uma unica renovacao serializada");
        shared.Invalidate(); await shared.GetAsync(default); Check(sessionTransport.Refreshes==2, "invalidacao solicita novo JWT sem senha");
        setup=Fixture();
        using(var cancelDuringUpload=new CancellationTokenSource())
        {
            api=new Api { CancelUpload=cancelDuringUpload };
            processor=new TelemetryUploadProcessor(setup.store,auth,new TelemetryUploadClient(api),"https://example.invalid");
            await Throws<OperationCanceledException>(()=>processor.ProcessAsync(cancelDuringUpload.Token));
            Check(setup.store.Read(setup.payload.SessionId).Status=="Pending" && api.Sessions.Count==0,"cancelar em transferencia conserva pendencia sem resumo parcial");
            api.CancelUpload=null; await processor.ProcessAsync(default,true);
            Check(setup.store.Read(setup.payload.SessionId).Status=="Uploaded" && api.Objects.Count==4,"transferencia cancelada e retomada sem novos caminhos");
        }

        // Arquivos reais produzidos pelo C# para o teste de interoperabilidade do Worker web.
        string interoperability = Path.GetFullPath("Temp/TelemetryUploadInterop"); Directory.CreateDirectory(interoperability);
        foreach (var file in package.Files) File.WriteAllBytes(Path.Combine(interoperability, file.Name), file.Bytes);
        File.WriteAllText(Path.Combine(interoperability, "Metadata.json"), package.RequestBody.ToString());
        return checks;
    }
    private static (TelemetryUploadStore store, TelemetrySessionPayload payload) Fixture()
    {
        var store = new TelemetryUploadStore(Path.GetFullPath("Temp/TelemetryUploadChecks/" + Guid.NewGuid().ToString("N")));
        var payload = new TelemetrySessionPayload { SessionId = Guid.NewGuid().ToString("N"), ParticipantCode = Participant, StartedUtc = "2026-11-01T10:00:00Z", EndedUtc = "2026-11-01T10:01:00Z", ApplicationVersion = "fixture", DurationSeconds = 60, Attempts = 2, CorrectAnswers = 1, Errors = 1 };
        payload.Events.Add(new TelemetryEventFrame { TimeSinceStart = 1, EventType = "task_correct", ObjectId = "fase_1", Details = "=fixture,\"quoted\"\nnext line" });
        payload.Events.Add(new TelemetryEventFrame { TimeSinceStart = 2, EventType = "task_error", ObjectId = "fase_1", Details = "fixture" });
        payload.HeadFrames.Add(new HeadTelemetryFrame { TimeSinceStart = 1, HeadPositionX = -1, HeadPositionY = 1, HeadRotationY = 70 });
        payload.ControllerFrames.Add(new ControllerTelemetryFrame { TimeSinceStart = 1, IsLeftTracked = true, LeftPosX = 2.5f, RightPosZ = -3, RightStickY = 0.75f });
        Save(store, payload); return (store, payload);
    }
    private static void Save(TelemetryUploadStore store, TelemetrySessionPayload payload)
    {
        string folder = store.Folder(payload.SessionId); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Session.json"), JsonConvert.SerializeObject(payload));
        File.WriteAllText(Path.Combine(folder, "Ready.json"), "{}");
    }
    private sealed class Auth : ISupabaseAccessSession
    {
        public int Invalidations;
        public SupabaseAuthSession Session = SupabaseAuthSession.FromAccessToken("fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes(new JObject { ["role"] = "authenticated", ["sub"] = Owner, ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }.ToString())).TrimEnd('=').Replace('+','-').Replace('/','_') + ".fixture", DateTimeOffset.UtcNow);
        public Task<SupabaseAuthSession> GetAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Session); }
        public void Invalidate() { Invalidations++; }
    }
    private sealed class MemorySessionStore : ISupabaseSessionStore
    {
        private string token="fixture-refresh";
        public string LoadRefreshToken()=>token;
        public void SaveRefreshToken(string value)=>token=value;
        public void Clear()=>token=null;
    }
    private sealed class RefreshTransport : ISupabaseSessionTransport
    {
        private readonly string access;
        public int Refreshes;
        public RefreshTransport(string access)=>this.access=access;
        public Task<AuthHttpResponse> SignInAsync(string body,CancellationToken token)=>throw new InvalidOperationException("Password should not be used for refresh.");
        public async Task<AuthHttpResponse> RefreshAsync(string body,CancellationToken token)
        {
            Refreshes++; await Task.Delay(2,token);
            return new AuthHttpResponse(200,new JObject { ["access_token"]=access,["refresh_token"]="rotated-fixture" }.ToString());
        }
    }
    private sealed class Api : ITelemetryUploadTransport
    {
        public Dictionary<string, byte[]> Objects = new Dictionary<string, byte[]>();
        public HashSet<string> Sessions = new HashSet<string>();
        public List<string> Tokens = new List<string>();
        public int EventCount, Calls, VerifiedDuplicates;
        public string FailFileOnce;
        public bool LoseFirstAck, MissingMigration, FirstUnauthorized, AuthorizedParticipant = true, CommitReached;
        public TaskCompletionSource<TelemetryHttpResponse> BlockCommit;
        public CancellationTokenSource CancelUpload;
        public TelemetryHttpResponse LastAck;
        public Task<TelemetryHttpResponse> SendAsync(string method, string path, string token, byte[] body, string type, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Tokens.Add(token);
            if (FirstUnauthorized) { FirstUnauthorized = false; return Task.FromResult(TelemetryHttpResponse.Json(401, "{}")); }
            if (path.EndsWith("participant_telemetry_access")) return Task.FromResult(TelemetryHttpResponse.Json(200, AuthorizedParticipant ? "true" : "false"));
            if (path.StartsWith("/storage/"))
            {
                if (method == "GET") { VerifiedDuplicates++; return Task.FromResult(new TelemetryHttpResponse(200, Objects[path.Replace("/object/authenticated/", "/object/")])); }
                if (path.EndsWith(FailFileOnce ?? "no-such-file")) { FailFileOnce = null; throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Fixture: network unavailable"); }
                if (Objects.ContainsKey(path)) return Task.FromResult(TelemetryHttpResponse.Json(409, "{\"error\":\"Duplicate\"}"));
                Objects[path] = body;
                if(CancelUpload!=null && Objects.Count==2) CancelUpload.Cancel();
                return Task.FromResult(TelemetryHttpResponse.Json(201, "{}"));
            }
            if (MissingMigration) return Task.FromResult(TelemetryHttpResponse.Json(404, "{}"));
            var request = JObject.Parse(Encoding.UTF8.GetString(body)); var session = request["p_session"];
            bool inserted = Sessions.Add((string)session["session_id"]);
            if (inserted) EventCount += ((JArray)request["p_events"]).Count;
            LastAck = TelemetryHttpResponse.Json(200, new JObject { ["session_id"] = session["session_id"], ["participant_code"] = session["participant_code"], ["digest"] = request["p_digest"], ["file_count"] = 4, ["event_count"] = ((JArray)request["p_events"]).Count, ["status"] = inserted ? "uploaded" : "already_uploaded" }.ToString());
            if (LoseFirstAck) { LoseFirstAck = false; throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Fixture: ACK lost after commit"); }
            if (BlockCommit != null) { CommitReached = true; return BlockCommit.Task; }
            return Task.FromResult(LastAck);
        }
    }
}
