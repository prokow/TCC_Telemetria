using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MainProjeto.Telemetria
{
    public class TelemetryControllerRecorder : MonoBehaviour
    {
        [Header("Transforms dos controladores rastreados pelo XR Origin")]
        [SerializeField] private Transform leftControllerTransform;
        [SerializeField] private Transform rightControllerTransform;
        [Header("Ações habilitadas pelo Input Action Manager")]
        [SerializeField] private InputActionReference leftTrackedAction;
        [SerializeField] private InputActionReference rightTrackedAction;
        [SerializeField] private InputActionReference leftStickAction;
        [SerializeField] private InputActionReference rightStickAction;
        [Header("Amostragem")]
        [Min(1f)] [SerializeField] private float recordRateHz = 10f;

        private readonly List<ControllerTelemetryFrame> history = new List<ControllerTelemetryFrame>();
        private bool recording;
        private double sessionStartRealtime;
        private double nextSampleRealtime;

        public bool CanRecord
        {
            get { return leftControllerTransform != null && rightControllerTransform != null; }
        }
        public List<ControllerTelemetryFrame> CopyHistory()
        {
            return new List<ControllerTelemetryFrame>(history);
        }

        public bool Begin(double sharedSessionStartRealtime)
        {
            if (!CanRecord)
            {
                Debug.LogError("[TelemetryControllerRecorder] Atribua os dois Transforms dos controladores.");
                return false;
            }
            ValidateAction(leftTrackedAction, "Left Tracked");
            ValidateAction(rightTrackedAction, "Right Tracked");
            ValidateAction(leftStickAction, "Left Stick");
            ValidateAction(rightStickAction, "Right Stick");
            history.Clear();
            sessionStartRealtime = sharedSessionStartRealtime;
            nextSampleRealtime = Time.realtimeSinceStartupAsDouble;
            recording = true;
            return true;
        }

        public void End() { recording = false; }

        private static void ValidateAction(InputActionReference reference, string label)
        {
            if (reference == null || reference.action == null || !reference.action.enabled)
                Debug.LogWarning("[TelemetryControllerRecorder] Ação não habilitada/atribuída: " + label);
        }

        private static bool ReadTracked(InputActionReference reference)
        {
            return reference != null && reference.action != null && reference.action.enabled
                && reference.action.ReadValue<float>() > 0.5f;
        }

        private static Vector2 ReadStick(InputActionReference reference)
        {
            if (reference == null || reference.action == null || !reference.action.enabled)
                return Vector2.zero;
            return reference.action.ReadValue<Vector2>();
        }

        private void Update()
        {
            if (!recording || !CanRecord) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextSampleRealtime) return;
            Vector3 lp = leftControllerTransform.position;
            Vector3 lr = leftControllerTransform.eulerAngles;
            Vector3 rp = rightControllerTransform.position;
            Vector3 rr = rightControllerTransform.eulerAngles;
            Vector2 ls = ReadStick(leftStickAction);
            Vector2 rs = ReadStick(rightStickAction);

            history.Add(new ControllerTelemetryFrame
            {
                TimeSinceStart = (float)(now - sessionStartRealtime),
                IsLeftTracked = ReadTracked(leftTrackedAction),
                LeftPosX = lp.x, LeftPosY = lp.y, LeftPosZ = lp.z,
                LeftRotX = lr.x, LeftRotY = lr.y, LeftRotZ = lr.z,
                LeftStickX = ls.x, LeftStickY = ls.y,
                IsRightTracked = ReadTracked(rightTrackedAction),
                RightPosX = rp.x, RightPosY = rp.y, RightPosZ = rp.z,
                RightRotX = rr.x, RightRotY = rr.y, RightRotZ = rr.z,
                RightStickX = rs.x, RightStickY = rs.y
            });
            nextSampleRealtime = now + 1.0 / Mathf.Max(1f, recordRateHz);
        }
    }
}
