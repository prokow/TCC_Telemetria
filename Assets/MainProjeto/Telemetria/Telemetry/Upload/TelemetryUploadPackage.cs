using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria.Upload
{
    public enum TelemetryUploadFailure { Connection, Authorization, Configuration, Conflict, InvalidData }
    public sealed class TelemetryUploadException : Exception
    {
        public TelemetryUploadFailure Failure { get; }
        public TelemetryUploadException(TelemetryUploadFailure failure, string message) : base(message) => Failure = failure;
    }
    public sealed class TelemetryUploadFile
    {
        public string Name, Type, StoragePath, ContentType, Sha256;
        public string FilePath;
        public long ByteSize;
        public JObject Metadata => new JObject { ["file_type"] = Type, ["bucket_id"] = "vr-telemetry", ["storage_path"] = StoragePath, ["byte_size"] = ByteSize, ["sha256"] = Sha256, ["content_type"] = ContentType };
    }
    public sealed class TelemetryUploadPackage
    {
        public string SessionId, ParticipantCode, OperatorId, Digest;
        public JObject Summary;
        public JArray Events;
        public List<TelemetryUploadFile> Files;
        public JObject RequestBody => new JObject { ["p_session"] = Summary, ["p_events"] = Events, ["p_files"] = new JArray(Files.Select(file => file.Metadata)), ["p_digest"] = Digest };
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static bool ValidCode(string code) => code != null && Regex.IsMatch(code, "^[0-9a-f]{32}$") && Guid.TryParseExact(code, "N", out _);
        private static void Require(bool valid) { if (!valid) throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "Dados locais invalidos. Confira os UUIDs e o arquivo Session.json."); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static TelemetryUploadPackage Build(string folder, string operatorId)
        {
            try { return BuildCore(folder, operatorId); }
            catch (Exception error) when (error is JsonException || error is InvalidDataException || error is FormatException || error is OverflowException)
            {
                throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "Session.json invalido ou incompleto. Os arquivos locais foram preservados.");
            }
        }
        private static TelemetryUploadPackage BuildCore(string folder, string operatorId)
        {
            Require(Guid.TryParseExact(operatorId, "D", out _));
            string jsonPath = Path.Combine(folder, "Session.json");
            Require(new FileInfo(jsonPath).Length > 0 && new FileInfo(jsonPath).Length <= TelemetrySessionWriter.MaxFileBytes);
            TelemetrySessionPayload payload;
            try { payload = TelemetryJsonStream.Read(jsonPath); }
            catch (JsonException) { throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "Session.json invalido. O arquivo local foi preservado."); }
            Require(payload != null && ValidCode(payload.SessionId) && ValidCode(payload.ParticipantCode) && Path.GetFileName(Path.GetFullPath(folder)) == payload.SessionId);
            Require(string.IsNullOrEmpty(payload.CollectorUserId) || payload.CollectorUserId == operatorId);
            Require(payload.Attempts >= 0 && payload.CorrectAnswers >= 0 && payload.Errors >= 0 && (long)payload.Attempts == (long)payload.CorrectAnswers + payload.Errors);
            Require(Finite(payload.DurationSeconds) && payload.DurationSeconds >= 0 && payload.DurationSeconds <= 604800 && payload.SchemaVersion >= 1 && payload.SchemaVersion <= 100);
            Require((payload.ApplicationVersion ?? "").Length <= 200 && !string.IsNullOrEmpty(payload.CoordinateSpace) && payload.CoordinateSpace.Length <= 100);
            Require(DateTimeOffset.TryParse(payload.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started) && DateTimeOffset.TryParse(payload.EndedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ended) && ended >= started);
            Require(Regex.IsMatch(payload.StartedUtc ?? "", "T.*(Z|[+-][0-9]{2}:[0-9]{2})$") && Regex.IsMatch(payload.EndedUtc ?? "", "T.*(Z|[+-][0-9]{2}:[0-9]{2})$"));

            var events = new JArray();
            int headCount = 0, controllerCount = 0, leftTracked = 0, rightTracked = 0;
            TelemetryJsonStream.Read(jsonPath,
                head: frame => {
                    Require(++headCount <= 10000000 && frame != null && Finite(frame.TimeSinceStart) && frame.TimeSinceStart >= 0 && frame.TimeSinceStart <= payload.DurationSeconds + 5 && Finite(frame.HeadPositionX) && Finite(frame.HeadPositionY) && Finite(frame.HeadPositionZ) && Finite(frame.HeadRotationX) && Finite(frame.HeadRotationY) && Finite(frame.HeadRotationZ));
                },
                controllers: frame => {
                    Require(++controllerCount <= 10000000 && frame != null && Finite(frame.TimeSinceStart) && frame.TimeSinceStart >= 0 && frame.TimeSinceStart <= payload.DurationSeconds + 5 && Finite(frame.LeftPosX) && Finite(frame.LeftPosY) && Finite(frame.LeftPosZ) && Finite(frame.RightPosX) && Finite(frame.RightPosY) && Finite(frame.RightPosZ) && Finite(frame.LeftRotX) && Finite(frame.LeftRotY) && Finite(frame.LeftRotZ) && Finite(frame.RightRotX) && Finite(frame.RightRotY) && Finite(frame.RightRotZ) && Finite(frame.LeftStickX) && Finite(frame.LeftStickY) && Finite(frame.RightStickX) && Finite(frame.RightStickY));
                    if (frame.IsLeftTracked) leftTracked++; if (frame.IsRightTracked) rightTracked++;
                },
                events: frame => {
                    Require(events.Count < TelemetrySessionWriter.MaxEvents && frame != null && Finite(frame.TimeSinceStart) && frame.TimeSinceStart >= 0 && frame.TimeSinceStart <= payload.DurationSeconds + 5 && !string.IsNullOrEmpty(frame.EventType) && frame.EventType.Length <= 120 && (frame.ObjectId ?? "").Length <= 200 && (frame.Details ?? "").Length <= 4000);
                    events.Add(new JObject { ["time_since_start"] = frame.TimeSinceStart, ["event_type"] = frame.EventType, ["object_id"] = frame.ObjectId ?? "", ["details"] = frame.Details ?? "" });
                });
            var package = new TelemetryUploadPackage { SessionId = payload.SessionId, ParticipantCode = payload.ParticipantCode, OperatorId = operatorId, Events = events, Files = new List<TelemetryUploadFile>() };
            string prefix = operatorId + "/" + payload.ParticipantCode + "/" + payload.SessionId + "/";
            PrepareCsvFiles(folder);
            foreach (string name in new[] { "Controllers.csv", "Events.csv", "Head.csv", "Session.json" })
            {
                string filePath = Path.Combine(folder, name);
                long length = new FileInfo(filePath).Length;
                Require(length > 0 && length <= TelemetrySessionWriter.MaxFileBytes);
                string type = name == "Session.json" ? "session_json" : name == "Head.csv" ? "head_csv" : name == "Controllers.csv" ? "controllers_csv" : "events_csv";
                package.Files.Add(new TelemetryUploadFile { Name = name, Type = type, FilePath = filePath, ByteSize = length, Sha256 = HashFile(filePath), StoragePath = prefix + name, ContentType = name == "Session.json" ? "application/json" : "text/csv" });
            }
            package.Digest = Hash(Encoding.UTF8.GetBytes(string.Join("\n", package.Files.Select(file => file.Name + ":" + file.Sha256))));
            package.Summary = new JObject { ["session_id"] = payload.SessionId, ["participant_code"] = payload.ParticipantCode, ["started_utc"] = payload.StartedUtc, ["ended_utc"] = payload.EndedUtc, ["duration_seconds"] = payload.DurationSeconds, ["attempts"] = payload.Attempts, ["correct_answers"] = payload.CorrectAnswers, ["errors"] = payload.Errors, ["application_version"] = payload.ApplicationVersion ?? "", ["schema_version"] = payload.SchemaVersion, ["coordinate_space"] = payload.CoordinateSpace,
                ["sample_counts"] = new JObject { ["head"] = headCount, ["controllers"] = controllerCount, ["left_tracked"] = leftTracked, ["right_tracked"] = rightTracked } };
            return package;
        }
        // A cache is usable only when canonical JSON and each derived file still match their hashes.
        public static void PrepareCsvFiles(string folder)
        {
            string jsonPath = Path.Combine(folder, "Session.json");
            string jsonHash = HashFile(jsonPath);
            string cachePath = Path.Combine(folder, "CsvCache.json");
            string[] names = { "Head.csv", "Controllers.csv", "Events.csv" };
            try
            {
                if (File.Exists(cachePath) && new FileInfo(cachePath).Length < 4096)
                {
                    var cache = JObject.Parse(File.ReadAllText(cachePath));
                    if ((int?)cache["version"] == 1 && (string)cache["json"] == jsonHash && names.All(n => File.Exists(Path.Combine(folder, n)) && (string)cache[n] == HashFile(Path.Combine(folder, n)))) return;
                }
            }
            catch (JsonException) { }
            var metadata = TelemetryJsonStream.Read(jsonPath);
            string prefix = TelemetryCsvEncoder.Prefix(metadata);
            using (var head = new StreamWriter(Path.Combine(folder, names[0] + ".tmp"), false, new UTF8Encoding(false), 16384))
            using (var controls = new StreamWriter(Path.Combine(folder, names[1] + ".tmp"), false, new UTF8Encoding(false), 16384))
            using (var events = new StreamWriter(Path.Combine(folder, names[2] + ".tmp"), false, new UTF8Encoding(false), 16384))
            {
                head.Write(TelemetryCsvEncoder.HeadHeader); controls.Write(TelemetryCsvEncoder.ControllerHeader); events.Write(TelemetryCsvEncoder.EventHeader);
                TelemetryJsonStream.Read(jsonPath, h => TelemetryCsvEncoder.Head(head, prefix, h), c => TelemetryCsvEncoder.Controller(controls, prefix, c), e => TelemetryCsvEncoder.Event(events, prefix, e));
            }
            var result = new JObject { ["version"] = 1, ["json"] = jsonHash };
            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                TelemetryUploadStore.CommitTemporary(path + ".tmp", path);
                result[name] = HashFile(path);
            }
            TelemetryUploadStore.WriteAtomic(cachePath, result.ToString(Formatting.None));
        }
    }
}
