using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MainProjeto.Telemetria
{
    public static class TelemetryCsvEncoder
    {
        private static readonly char[] Special = { ',', '"', '\n', '\r' };
        public const string HeadHeader = "ParticipantCode,SessionId,TimeSinceStart,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ\n";
        public const string ControllerHeader = "ParticipantCode,SessionId,TimeSinceStart,IsLeftTracked,LeftPosX,LeftPosY,LeftPosZ,LeftRotX,LeftRotY,LeftRotZ,LeftStickX,LeftStickY,IsRightTracked,RightPosX,RightPosY,RightPosZ,RightRotX,RightRotY,RightRotZ,RightStickX,RightStickY\n";
        public const string EventHeader = "ParticipantCode,SessionId,TimeSinceStart,EventType,ObjectId,Details\n";
        private static string Cell(string value)
        {
            value = value ?? string.Empty;
            string trimmed = value.TrimStart();
            if ((trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) || value.StartsWith("\t") || value.StartsWith("\r")) value = "'" + value;
            return value.IndexOfAny(Special) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
        public static string Prefix(TelemetrySessionPayload payload) => Cell(payload.ParticipantCode) + "," + Cell(payload.SessionId) + ",";
        private static void Number(TextWriter writer, float value) { writer.Write(','); writer.Write(value.ToString("R", CultureInfo.InvariantCulture)); }
        public static void Head(TextWriter w, string prefix, HeadTelemetryFrame f)
        {
            w.Write(prefix); w.Write(f.TimeSinceStart.ToString("R", CultureInfo.InvariantCulture));
            Number(w, f.HeadPositionX); Number(w, f.HeadPositionY); Number(w, f.HeadPositionZ);
            Number(w, f.HeadRotationX); Number(w, f.HeadRotationY); Number(w, f.HeadRotationZ); w.Write('\n');
        }
        public static void Controller(TextWriter w, string prefix, ControllerTelemetryFrame f)
        {
            w.Write(prefix); w.Write(f.TimeSinceStart.ToString("R", CultureInfo.InvariantCulture)); w.Write(f.IsLeftTracked ? ",1" : ",0");
            Number(w, f.LeftPosX); Number(w, f.LeftPosY); Number(w, f.LeftPosZ); Number(w, f.LeftRotX); Number(w, f.LeftRotY); Number(w, f.LeftRotZ); Number(w, f.LeftStickX); Number(w, f.LeftStickY);
            w.Write(f.IsRightTracked ? ",1" : ",0");
            Number(w, f.RightPosX); Number(w, f.RightPosY); Number(w, f.RightPosZ); Number(w, f.RightRotX); Number(w, f.RightRotY); Number(w, f.RightRotZ); Number(w, f.RightStickX); Number(w, f.RightStickY); w.Write('\n');
        }
        public static void Event(TextWriter w, string prefix, TelemetryEventFrame f)
        {
            w.Write(prefix); w.Write(f.TimeSinceStart.ToString("R", CultureInfo.InvariantCulture)); w.Write(','); w.Write(Cell(f.EventType)); w.Write(','); w.Write(Cell(f.ObjectId)); w.Write(','); w.Write(Cell(f.Details)); w.Write('\n');
        }
        // Compatibility helper for small consumers/tests. Production writes through TextWriter.
        public static Dictionary<string, string> Encode(TelemetrySessionPayload payload)
        {
            string prefix = Prefix(payload);
            using (var head = new StringWriter(CultureInfo.InvariantCulture))
            using (var controllers = new StringWriter(CultureInfo.InvariantCulture))
            using (var events = new StringWriter(CultureInfo.InvariantCulture))
            {
                head.Write(HeadHeader); controllers.Write(ControllerHeader); events.Write(EventHeader);
                foreach (var f in payload.HeadFrames) Head(head, prefix, f);
                foreach (var f in payload.ControllerFrames) Controller(controllers, prefix, f);
                foreach (var f in payload.Events) Event(events, prefix, f);
                return new Dictionary<string, string> { ["Head.csv"] = head.ToString(), ["Controllers.csv"] = controllers.ToString(), ["Events.csv"] = events.ToString() };
            }
        }
    }
}