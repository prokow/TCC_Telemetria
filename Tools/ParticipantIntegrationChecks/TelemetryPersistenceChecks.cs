using System;
using System.IO;
using System.Linq;
using System.Text;
using MainProjeto.Telemetria;
using MainProjeto.Telemetria.Upload;
using Newtonsoft.Json;

internal static class TelemetryPersistenceChecks
{
    private static int checks;
    private const string Owner = "00000000-0000-0000-0000-000000000001";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL Persistence: " + message); checks++; }
    private static TelemetrySessionPayload Batch(string id, float time)
    {
        var payload = new TelemetrySessionPayload { SessionId = id, ParticipantCode = "00000000000000000000000000000002", CollectorUserId = Owner,
            StartedUtc = "2026-11-01T10:00:00Z", EndedUtc = "2026-11-01T11:00:00Z", DurationSeconds = time, ApplicationVersion = "checkpoint-test" };
        payload.HeadFrames.Add(new HeadTelemetryFrame { TimeSinceStart = time, HeadPositionX = -1.25f });
        payload.ControllerFrames.Add(new ControllerTelemetryFrame { TimeSinceStart = time, IsLeftTracked = true, RightStickY = 0.75f });
        payload.Events.Add(new TelemetryEventFrame { TimeSinceStart = time, EventType = "test", ObjectId = "task", Details = "=formula,\"quote\"\nline" });
        return payload;
    }
    private static string NewFolder(string root) { string path = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    public static int Run()
    {
        string root = Path.GetFullPath("Temp/TelemetryPersistenceChecks/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string folder = NewFolder(root), id = Path.GetFileName(folder);
        using (var writer = new TelemetrySessionWriter(folder))
        {
            writer.Append(Batch(id, 1)); writer.Append(Batch(id, 2));
            Check(!File.Exists(Path.Combine(folder, "Session.json")), "coleta nao publica sessao parcial");
            Check(TelemetrySessionWriter.Recover(root).Count == 0, "recuperacao ignora gravador ativo");
            var end = Batch(id, 3); writer.Append(end); writer.Complete(end);
            var payload = JsonConvert.DeserializeObject<TelemetrySessionPayload>(File.ReadAllText(Path.Combine(folder, "Session.json")));
            Check(payload.HeadFrames.Count == 3 && payload.ControllerFrames.Count == 3 && payload.Events.Count == 3, "blocos unidos sem perdas ou duplicacao");
            Check(payload.HeadFrames.Select(f => f.TimeSinceStart).SequenceEqual(new[] { 1f, 2f, 3f }), "ordem temporal preservada");
            Check(payload.DurationSeconds == 3 && payload.CollectorUserId == Owner, "metadados finais preservados");
            foreach (var csv in TelemetryCsvEncoder.Encode(payload)) Check(File.ReadAllText(Path.Combine(folder, csv.Key)) == csv.Value, "CSV streaming compativel: " + csv.Key);
            Check(Directory.GetFiles(Path.Combine(folder, "Chunks"), "*.json").Length == 0 && File.Exists(Path.Combine(folder, "Ready.json")), "checkpoints liberados somente apos commit completo");
            var first = TelemetryUploadPackage.Build(folder, Owner); var csvTime = File.GetLastWriteTimeUtc(Path.Combine(folder, "Head.csv"));
            var again = TelemetryUploadPackage.Build(folder, Owner);
            Check(first.Digest == again.Digest && File.GetLastWriteTimeUtc(Path.Combine(folder, "Head.csv")) == csvTime, "reenvio reutiliza CSVs confirmados");
            File.WriteAllText(Path.Combine(folder, "Head.csv"), "corrupt");
            Check(TelemetryUploadPackage.Build(folder, Owner).Digest == first.Digest, "cache adulterado e reconstruido do JSON canonico");
        }
        folder = NewFolder(root); id = Path.GetFileName(folder);
        using (var writer = new TelemetrySessionWriter(folder)) { writer.Append(Batch(id, 1)); writer.Append(Batch(id, 2)); }
        File.WriteAllText(Path.Combine(folder, "Chunks", "00000002.json.tmp"), "{truncated");
        Check(TelemetrySessionWriter.Recover(root).Count == 1, "recupera sessao interrompida");
        var recovered = JsonConvert.DeserializeObject<TelemetrySessionPayload>(File.ReadAllText(Path.Combine(folder, "Session.json")));
        Check(recovered.HeadFrames.Count == 2 && recovered.DurationSeconds == 2, "recuperacao usa somente checkpoints confirmados");
        Check(recovered.Events.Last().EventType == "session_recovered", "sessao recuperada informa possivel cauda perdida");
        Check(TelemetrySessionWriter.Recover(root).Count == 0, "recuperacao idempotente");
        Check(TelemetryUploadPackage.Build(folder, Owner).Files.Count == 4, "sessao recuperada continua aceita no uploader");

        folder = NewFolder(root); id = Path.GetFileName(folder);
        using (var writer = new TelemetrySessionWriter(folder))
        {
            string blocked = Path.Combine(folder, "Chunks", "00000000.json.tmp"); Directory.CreateDirectory(blocked);
            bool failed = false; try { writer.Append(Batch(id, 1)); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Check(failed && writer.StoredBytes == 0, "falha no checkpoint nao avanca sequencia");
            Directory.Delete(blocked); writer.Append(Batch(id, 1));
            string blockedCsv = Path.Combine(folder, "Head.csv.tmp"); Directory.CreateDirectory(blockedCsv);
            failed = false; try { writer.Complete(Batch(id, 1)); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Check(failed && !File.Exists(Path.Combine(folder, "Ready.json")) && File.Exists(Path.Combine(folder, "Chunks", "00000000.json")), "falha no CSV conserva checkpoint e nao publica ready");
            string originalHash = TelemetryUploadPackage.HashFile(Path.Combine(folder, "Session.json"));
            Directory.Delete(blockedCsv); writer.Complete(Batch(id, 1));
            Check(TelemetryUploadPackage.HashFile(Path.Combine(folder, "Session.json")) == originalHash, "retry preserva JSON canonico");
            Check((int)TelemetryUploadPackage.Build(folder, Owner).Summary["sample_counts"]["head"] == 1, "retry sem amostra duplicada");
        }
        folder = NewFolder(root); id = Path.GetFileName(folder);
        using (var writer = new TelemetrySessionWriter(folder)) { writer.Append(Batch(id, 1)); writer.Append(Batch(id, 2)); }
        File.Move(Path.Combine(folder, "Chunks", "00000001.json"), Path.Combine(folder, "Chunks", "00000002.json"));
        TelemetrySessionWriter.Recover(root);
        Check(!File.Exists(Path.Combine(folder, "Ready.json")), "lacuna nos checkpoints nao e apresentada como sessao completa");

        // Valid JSON above the old 32 MiB limit; whitespace avoids large fixtures in managed memory.
        folder = NewFolder(root); id = Path.GetFileName(folder);
        string jsonPath = Path.Combine(folder, "Session.json");
        using (var stream = new StreamWriter(jsonPath, false, new UTF8Encoding(false)))
        {
            stream.Write(JsonConvert.SerializeObject(Batch(id, 1)));
            string padding = new string(' ', 4096);
            for (int i = 0; i < 8448; i++) stream.Write(padding);
        }
        long before = GC.GetTotalMemory(true);
        var large = TelemetryUploadPackage.Build(folder, Owner);
        long retained = GC.GetTotalMemory(true) - before;
        Check(large.Files.Single(f => f.Name == "Session.json").ByteSize > 32L * 1024 * 1024, "arquivo acima de 32 MiB aceito ate limite do bucket");
        Check(retained < 4L * 1024 * 1024, "pacote nao retem arquivo de 33 MiB em RAM");
        GC.KeepAlive(large);
        using (var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Write)) stream.SetLength(TelemetrySessionWriter.MaxFileBytes + 1);
        bool rejected = false;
        try { TelemetryUploadPackage.Build(folder, Owner); } catch (TelemetryUploadException e) { rejected = e.Failure == TelemetryUploadFailure.InvalidData; }
        Check(rejected, "arquivo acima de 64 MiB recusado antes do parsing");
        // The streaming reader must reject malformed arrays, not silently return empty samples.
        foreach (string invalid in new[] {
            "{\"HeadFrames\":[],\"ControllerFrames\":[],\"Events\":[null]}",
            "{\"HeadFrames\":[],\"HeadFrames\":[],\"ControllerFrames\":[],\"Events\":[]}",
            "{\"HeadFrames\":[],\"ControllerFrames\":[]}",
            "{\"HeadFrames\":[{\"TimeSinceStart\":0}"
        })
        {
            File.WriteAllText(jsonPath, invalid);
            bool invalidRejected = false;
            try { TelemetryJsonStream.Read(jsonPath, h => { }, c => { }, e => { }); }
            catch (InvalidDataException) { invalidRejected = true; }
            catch (JsonException) { invalidRejected = true; }
            Check(invalidRejected, "JSON incompleto/duplicado/nulo rejeitado no streaming");
        }
        return checks;
    }
}