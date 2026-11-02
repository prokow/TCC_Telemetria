using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MainProjeto.Telemetria
{
    public static class TelemetryCsvEncoder
    {
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Cell(string value)
        {
            value = value ?? string.Empty;
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0 || value.StartsWith("\t") || value.StartsWith("\r")) value = "'" + value;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
        public static Dictionary<string, string> Encode(TelemetrySessionPayload payload)
        {
            string prefix = Cell(payload.ParticipantCode) + "," + Cell(payload.SessionId) + ",";
            var head = new StringBuilder("ParticipantCode,SessionId,TimeSinceStart,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ\n");
            foreach (var frame in payload.HeadFrames)
                head.Append(prefix).Append(string.Join(",", new[] { F(frame.TimeSinceStart), F(frame.HeadPositionX), F(frame.HeadPositionY), F(frame.HeadPositionZ), F(frame.HeadRotationX), F(frame.HeadRotationY), F(frame.HeadRotationZ) })).Append('\n');
            var controllers = new StringBuilder("ParticipantCode,SessionId,TimeSinceStart,IsLeftTracked,LeftPosX,LeftPosY,LeftPosZ,LeftRotX,LeftRotY,LeftRotZ,LeftStickX,LeftStickY,IsRightTracked,RightPosX,RightPosY,RightPosZ,RightRotX,RightRotY,RightRotZ,RightStickX,RightStickY\n");
            foreach (var frame in payload.ControllerFrames)
                controllers.Append(prefix).Append(string.Join(",", new[] { F(frame.TimeSinceStart), frame.IsLeftTracked ? "1" : "0", F(frame.LeftPosX), F(frame.LeftPosY), F(frame.LeftPosZ), F(frame.LeftRotX), F(frame.LeftRotY), F(frame.LeftRotZ), F(frame.LeftStickX), F(frame.LeftStickY), frame.IsRightTracked ? "1" : "0", F(frame.RightPosX), F(frame.RightPosY), F(frame.RightPosZ), F(frame.RightRotX), F(frame.RightRotY), F(frame.RightRotZ), F(frame.RightStickX), F(frame.RightStickY) })).Append('\n');
            var events = new StringBuilder("ParticipantCode,SessionId,TimeSinceStart,EventType,ObjectId,Details\n");
            foreach (var frame in payload.Events) events.Append(prefix).Append(F(frame.TimeSinceStart)).Append(',').Append(Cell(frame.EventType)).Append(',').Append(Cell(frame.ObjectId)).Append(',').Append(Cell(frame.Details)).Append('\n');
            return new Dictionary<string, string> { ["Head.csv"] = head.ToString(), ["Controllers.csv"] = controllers.ToString(), ["Events.csv"] = events.ToString() };
        }
    }
}
