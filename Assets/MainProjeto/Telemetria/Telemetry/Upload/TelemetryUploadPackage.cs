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
        public byte[] Bytes;
        public JObject Metadata => new JObject { ["file_type"] = Type, ["bucket_id"] = "vr-telemetry", ["storage_path"] = StoragePath, ["byte_size"] = Bytes.LongLength, ["sha256"] = Sha256, ["content_type"] = ContentType };
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
        public static bool ValidCode(string code) => code != null && Regex.IsMatch(code, "^[0-9a-f]{32}$") && Guid.TryParseExact(code, "N", out _);
        private static void Require(bool valid) { if (!valid) throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "Dados locais invalidos. Confira os UUIDs e o arquivo Session.json."); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static TelemetryUploadPackage Build(string folder, string operatorId)
        {
            Require(Guid.TryParseExact(operatorId, "D", out _));
            byte[] json = File.ReadAllBytes(Path.Combine(folder, "Session.json"));
            Require(json.Length > 0 && json.Length <= 32 * 1024 * 1024);
            TelemetrySessionPayload payload;
            try { payload = JsonConvert.DeserializeObject<TelemetrySessionPayload>(Encoding.UTF8.GetString(json)); }
            catch (JsonException) { throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "Session.json invalido. O arquivo local foi preservado."); }
            Require(payload != null && ValidCode(payload.SessionId) && ValidCode(payload.ParticipantCode) && Path.GetFileName(Path.GetFullPath(folder)) == payload.SessionId);
            Require(string.IsNullOrEmpty(payload.CollectorUserId) || payload.CollectorUserId == operatorId);
            Require(payload.Attempts >= 0 && payload.CorrectAnswers >= 0 && payload.Errors >= 0 && (long)payload.Attempts == (long)payload.CorrectAnswers + payload.Errors);
            Require(Finite(payload.DurationSeconds) && payload.DurationSeconds >= 0 && payload.DurationSeconds <= 604800 && payload.SchemaVersion >= 1 && payload.SchemaVersion <= 100);
            Require((payload.ApplicationVersion ?? "").Length <= 200 && !string.IsNullOrEmpty(payload.CoordinateSpace) && payload.CoordinateSpace.Length <= 100);
            Require(DateTimeOffset.TryParse(payload.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started) && DateTimeOffset.TryParse(payload.EndedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ended) && ended >= started);
            Require(Regex.IsMatch(payload.StartedUtc ?? "", "T.*(Z|[+-][0-9]{2}:[0-9]{2})$") && Regex.IsMatch(payload.EndedUtc ?? "", "T.*(Z|[+-][0-9]{2}:[0-9]{2})$"));
            Require(payload.Events != null && payload.Events.Count <= 10000 && payload.HeadFrames != null && payload.ControllerFrames != null);
            Require(payload.HeadFrames.Count <= 10000000 && payload.ControllerFrames.Count <= 10000000);
            var events = new JArray();
            foreach (var frame in payload.Events)
            {
                Require(frame != null && Finite(frame.TimeSinceStart) && frame.TimeSinceStart >= 0 && frame.TimeSinceStart <= payload.DurationSeconds + 5 && !string.IsNullOrEmpty(frame.EventType) && frame.EventType.Length <= 120 && (frame.ObjectId ?? "").Length <= 200 && (frame.Details ?? "").Length <= 4000);
                events.Add(new JObject { ["time_since_start"] = frame.TimeSinceStart, ["event_type"] = frame.EventType, ["object_id"] = frame.ObjectId ?? "", ["details"] = frame.Details ?? "" });
            }
            Require(payload.HeadFrames.All(frame => frame != null && Finite(frame.TimeSinceStart) && frame.TimeSinceStart >= 0 && frame.TimeSinceStart <= payload.DurationSeconds + 5 && Finite(frame.HeadPositionX) && Finite(frame.HeadPositionY) && Finite(frame.HeadPositionZ) && Finite(frame.HeadRotationX) && Finite(frame.HeadRotationY) && Finite(frame.HeadRotationZ)));
            Require(payload.ControllerFrames.All(frame => frame != null && Finite(frame.TimeSinceStart) && frame.TimeSinceStart >= 0 && frame.TimeSinceStart <= payload.DurationSeconds + 5 && Finite(frame.LeftPosX) && Finite(frame.LeftPosY) && Finite(frame.LeftPosZ) && Finite(frame.RightPosX) && Finite(frame.RightPosY) && Finite(frame.RightPosZ) && Finite(frame.LeftRotX) && Finite(frame.LeftRotY) && Finite(frame.LeftRotZ) && Finite(frame.RightRotX) && Finite(frame.RightRotY) && Finite(frame.RightRotZ) && Finite(frame.LeftStickX) && Finite(frame.LeftStickY) && Finite(frame.RightStickX) && Finite(frame.RightStickY)));
            var package = new TelemetryUploadPackage { SessionId = payload.SessionId, ParticipantCode = payload.ParticipantCode, OperatorId = operatorId, Events = events, Files = new List<TelemetryUploadFile>() };
            string prefix = operatorId + "/" + payload.ParticipantCode + "/" + payload.SessionId + "/";
            var contents = TelemetryCsvEncoder.Encode(payload).ToDictionary(pair => pair.Key, pair => Encoding.UTF8.GetBytes(pair.Value));
            contents.Add("Session.json", json);
            foreach (var pair in contents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Require(pair.Value.Length > 0 && pair.Value.Length <= 64 * 1024 * 1024);
                string type = pair.Key == "Session.json" ? "session_json" : pair.Key == "Head.csv" ? "head_csv" : pair.Key == "Controllers.csv" ? "controllers_csv" : "events_csv";
                package.Files.Add(new TelemetryUploadFile { Name = pair.Key, Type = type, Bytes = pair.Value, Sha256 = Hash(pair.Value), StoragePath = prefix + pair.Key, ContentType = pair.Key == "Session.json" ? "application/json" : "text/csv" });
            }
            package.Digest = Hash(Encoding.UTF8.GetBytes(string.Join("\n", package.Files.Select(file => file.Name + ":" + file.Sha256))));
            package.Summary = new JObject { ["session_id"] = payload.SessionId, ["participant_code"] = payload.ParticipantCode, ["started_utc"] = payload.StartedUtc, ["ended_utc"] = payload.EndedUtc, ["duration_seconds"] = payload.DurationSeconds, ["attempts"] = payload.Attempts, ["correct_answers"] = payload.CorrectAnswers, ["errors"] = payload.Errors, ["application_version"] = payload.ApplicationVersion ?? "", ["schema_version"] = payload.SchemaVersion, ["coordinate_space"] = payload.CoordinateSpace,
                ["sample_counts"] = new JObject { ["head"] = payload.HeadFrames.Count, ["controllers"] = payload.ControllerFrames.Count, ["left_tracked"] = payload.ControllerFrames.Count(frame => frame.IsLeftTracked), ["right_tracked"] = payload.ControllerFrames.Count(frame => frame.IsRightTracked) } };
            return package;
        }
    }
}
