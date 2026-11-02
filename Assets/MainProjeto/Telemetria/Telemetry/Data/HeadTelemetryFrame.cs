using System;

namespace MainProjeto.Telemetria
{
    // Mede pose do headset; NÃO é rastreamento ocular (eye tracking).
    [Serializable]
    public class HeadTelemetryFrame
    {
        public float TimeSinceStart;
        public float HeadPositionX, HeadPositionY, HeadPositionZ;
        public float HeadRotationX, HeadRotationY, HeadRotationZ;
    }
}
