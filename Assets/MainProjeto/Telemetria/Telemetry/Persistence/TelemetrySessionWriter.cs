using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MainProjeto.Telemetria.Upload;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria
{
    // Single worker owns this writer. Only completed, atomically renamed chunks are recoverable.
    public sealed class TelemetrySessionWriter : IDisposable
    {
        public const int MaxBufferedFrames = 600;
        public const int MaxBufferedEvents = 128;
        public const int MaxEvents = 10000;
        public const long StopAtBytes = 56L * 1024 * 1024;
        public const long MaxFileBytes = 64L * 1024 * 1024;
        public const long MaxChunkBytes = 4L * 1024 * 1024;
        private static readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string folder;
        private int sequence;
        private long storedBytes;
        public long StoredBytes => System.Threading.Interlocked.Read(ref storedBytes);
        public string Folder => folder;
        public TelemetrySessionWriter(string folder)
        {
            this.folder = Path.GetFullPath(folder);
            lock (Active) { if (!Active.Add(this.folder)) throw new InvalidOperationException("Sessao ja possui um gravador."); }
        }
        public void Append(TelemetrySessionPayload batch)
        {
            string chunks = Path.Combine(folder, "Chunks");
            Directory.CreateDirectory(chunks);
            string path = Path.Combine(chunks, sequence.ToString("D8", CultureInfo.InvariantCulture) + ".json");
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var text = new StreamWriter(file, new UTF8Encoding(false), 16384, true))
                using (var json = new JsonTextWriter(text)) JsonSerializer.CreateDefault().Serialize(json, batch);
                if (file.Length > MaxChunkBytes) throw new InvalidDataException("Bloco de telemetria excede o limite de seguranca.");
                file.Flush(true);
            }
            TelemetryUploadStore.CommitTemporary(temp, path);
            System.Threading.Interlocked.Add(ref storedBytes, new FileInfo(path).Length);
            sequence++;
        }
        public string Complete(TelemetrySessionPayload metadata, bool recovered = false)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Session.json");
            if (!File.Exists(path))
            {
                string[] chunks = Directory.GetFiles(Path.Combine(folder, "Chunks"), "*.json").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (chunks.Length == 0) throw new InvalidDataException("Nenhum checkpoint disponivel.");
                string temp = path + ".tmp";
                var serializer = JsonSerializer.CreateDefault();
                // The metadata object has empty lists; samples are streamed from bounded chunks below.
                JObject header = JObject.FromObject(metadata);
                header.Remove("HeadFrames"); header.Remove("ControllerFrames"); header.Remove("Events");
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var text = new StreamWriter(file, new UTF8Encoding(false), 16384, true))
                    using (var json = new JsonTextWriter(text))
                    {
                        json.WriteStartObject();
                        foreach (var property in header.Properties()) property.WriteTo(json);
                        json.WritePropertyName("HeadFrames"); json.WriteStartArray();
                        foreach (string chunk in chunks) TelemetryJsonStream.Read(chunk, head: f => serializer.Serialize(json, f));
                        json.WriteEndArray(); json.WritePropertyName("ControllerFrames"); json.WriteStartArray();
                        foreach (string chunk in chunks) TelemetryJsonStream.Read(chunk, controllers: f => serializer.Serialize(json, f));
                        json.WriteEndArray(); json.WritePropertyName("Events"); json.WriteStartArray();
                        int count = 0;
                        foreach (string chunk in chunks) TelemetryJsonStream.Read(chunk, events: f => { serializer.Serialize(json, f); count++; });
                        if (recovered && count < MaxEvents) serializer.Serialize(json, new TelemetryEventFrame { TimeSinceStart = metadata.DurationSeconds, EventType = "session_recovered", ObjectId = "application", Details = "Recuperada ate o ultimo checkpoint confirmado; a cauda nao gravada pode estar ausente." });
                        json.WriteEndArray(); json.WriteEndObject();
                    }
                    file.Flush(true);
                }
                File.Move(temp, path); // finalized JSON is immutable across retries
            }
            // Streaming validation/export; no upload or authentication required to finalize locally.
            TelemetryUploadPackage.PrepareCsvFiles(folder);
            TelemetryUploadStore.WriteAtomic(Path.Combine(folder, "Ready.json"), "{\"ready\":true}");
            // Safe only after JSON + CSVs + ready marker are committed.
            string chunkFolder = Path.Combine(folder, "Chunks");
            if (Directory.Exists(chunkFolder))
                foreach (string chunk in Directory.EnumerateFiles(chunkFolder))
                    if (Path.GetExtension(chunk) == ".json" || chunk.EndsWith(".json.tmp", StringComparison.Ordinal))
                        try { File.Delete(chunk); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return path;
        }
        public static List<string> Recover(string root)
        {
            var results = new List<string>();
            if (!Directory.Exists(root)) return results;
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                if (!TelemetryUploadPackage.ValidCode(Path.GetFileName(directory)) || File.Exists(Path.Combine(directory, "Ready.json")) || !Directory.Exists(Path.Combine(directory, "Chunks"))) continue;
                TelemetrySessionWriter writer;
                try { writer = new TelemetrySessionWriter(directory); } catch (InvalidOperationException) { continue; }
                using (writer)
                {
                    try
                    {
                        var chunks = Directory.GetFiles(Path.Combine(directory, "Chunks"), "*.json").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                        if (chunks.Length == 0) continue;
                        // Reject a gap instead of presenting partial data as a complete checkpoint sequence.
                        for (int i = 0; i < chunks.Length; i++)
                            if (Path.GetFileName(chunks[i]) != i.ToString("D8", CultureInfo.InvariantCulture) + ".json" || new FileInfo(chunks[i]).Length > MaxChunkBytes)
                                throw new InvalidDataException("Sequencia de checkpoints incompleta.");
                        var metadata = TelemetryJsonStream.Read(chunks[chunks.Length - 1]);
                        writer.Complete(metadata, true);
                        results.Add("Sessao recuperada: " + metadata.SessionId);
                    }
                    catch (Exception error) { results.Add("Checkpoints preservados para recuperacao: " + Path.GetFileName(directory) + " (" + error.GetType().Name + ")"); }
                }
            }
            return results;
        }
        public void Dispose() { lock (Active) Active.Remove(folder); }
    }
}