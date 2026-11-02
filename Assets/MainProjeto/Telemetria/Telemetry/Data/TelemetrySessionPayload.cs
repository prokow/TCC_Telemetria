using System;
using System.Collections.Generic;

namespace MainProjeto.Telemetria
{
    // JsonUtility serializa campos públicos em tipos marcados como Serializable.
    [Serializable]
    public class TelemetrySessionPayload
    {
        public int SchemaVersion = 2;
        public string SessionId;
        public string ParticipantCode;
        public string ParticipantDisplayName; // opcional; pode ser dado pessoal
        public string CollectorUserId;
        public string SupabaseProjectOrigin;
        public string StartedUtc;
        public string EndedUtc;
        public string ApplicationVersion;
        public string CoordinateSpace = "World";
        public float DurationSeconds;
        public int Attempts;
        public int CorrectAnswers;
        public int Errors;
        public List<HeadTelemetryFrame> HeadFrames = new List<HeadTelemetryFrame>();
        public List<ControllerTelemetryFrame> ControllerFrames = new List<ControllerTelemetryFrame>();
        public List<TelemetryEventFrame> Events = new List<TelemetryEventFrame>();
    }
}
