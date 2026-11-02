using System.Collections.Generic;
using UnityEngine;

namespace MainProjeto.Telemetria
{
    public class TelemetryHeadRecorder : MonoBehaviour
    {
        [Header("XR Origin > Camera Offset > Main Camera")]
        [SerializeField] private Transform headTransform;
        [Header("Amostragem")]
        [Min(1f)] [SerializeField] private float recordRateHz = 10f;

        private readonly List<HeadTelemetryFrame> history = new List<HeadTelemetryFrame>();
        private bool recording;
        private double sessionStartRealtime;
        private double nextSampleRealtime;

        public bool CanRecord { get { return headTransform != null; } }
        public List<HeadTelemetryFrame> CopyHistory() { return new List<HeadTelemetryFrame>(history); }

        public bool Begin(double sharedSessionStartRealtime)
        {
            if (!CanRecord)
            {
                Debug.LogError("[TelemetryHeadRecorder] Referência da Main Camera ausente.");
                return false;
            }
            history.Clear();
            sessionStartRealtime = sharedSessionStartRealtime;
            nextSampleRealtime = Time.realtimeSinceStartupAsDouble;
            recording = true;
            return true;
        }

        public void End()
        {
            recording = false;
        }

        private void Update()
        {
            if (!recording || headTransform == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextSampleRealtime) return;

            Vector3 position = headTransform.position;
            Vector3 rotation = headTransform.eulerAngles;
            history.Add(new HeadTelemetryFrame
            {
                TimeSinceStart = (float)(now - sessionStartRealtime),
                HeadPositionX = position.x, HeadPositionY = position.y, HeadPositionZ = position.z,
                HeadRotationX = rotation.x, HeadRotationY = rotation.y, HeadRotationZ = rotation.z
            });

            // Não fabrica amostras caso o FPS caia.
            nextSampleRealtime = now + 1.0 / Mathf.Max(1f, recordRateHz);
        }
    }
}
