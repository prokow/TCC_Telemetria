using System;

namespace MainProjeto.Telemetria
{
    [Serializable]
    public class ControllerTelemetryFrame
    {
        public float TimeSinceStart;
        public bool IsLeftTracked;
        public float LeftPosX, LeftPosY, LeftPosZ;
        public float LeftRotX, LeftRotY, LeftRotZ;
        public float LeftStickX, LeftStickY;
        public bool IsRightTracked;
        public float RightPosX, RightPosY, RightPosZ;
        public float RightRotX, RightRotY, RightRotZ;
        public float RightStickX, RightStickY;
    }
}
