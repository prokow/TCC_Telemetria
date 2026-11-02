using System;

namespace MainProjeto.Telemetria
{
    [Serializable]
    public class TelemetryEventFrame
    {
        public float TimeSinceStart;
        public string EventType;
        public string ObjectId;
        public string Details;
    }
}
