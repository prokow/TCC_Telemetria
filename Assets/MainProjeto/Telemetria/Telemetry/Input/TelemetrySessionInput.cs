using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace MainProjeto.Telemetria
{
    // Interface: não guarda estado próprio, lê o estado do TelemetrySessionManager.
    public class TelemetrySessionInput : MonoBehaviour
    {
        [SerializeField] private TelemetrySessionManager sessionManager;
        [SerializeField] private InputActionReference toggleSessionAction;
        [SerializeField] private Key editorFallbackKey = Key.T;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private AudioSource feedbackAudio;
        [SerializeField] private AudioClip startedClip;
        [SerializeField] private AudioClip stoppedClip;
        [Min(0f)] [SerializeField] private float minToggleInterval = 1f;
        private double lastToggleRealtime = -1000;
        private bool enabledActionHere;

        private void OnEnable()
        {
            if (sessionManager != null) sessionManager.StatusChanged += UpdateStatus;
            if (toggleSessionAction != null && toggleSessionAction.action != null &&
                !toggleSessionAction.action.enabled)
            {
                toggleSessionAction.action.Enable();
                enabledActionHere = true;
            }
        }
        private void OnDisable()
        {
            if (sessionManager != null) sessionManager.StatusChanged -= UpdateStatus;
            if (enabledActionHere && toggleSessionAction != null && toggleSessionAction.action != null)
                toggleSessionAction.action.Disable();
            enabledActionHere = false;
        }
        private void Update()
        {
            bool fromAction = toggleSessionAction != null && toggleSessionAction.action != null &&
                toggleSessionAction.action.WasPerformedThisFrame();
            bool fromKeyboard = Keyboard.current != null &&
                Keyboard.current[editorFallbackKey].wasPressedThisFrame;
            if (fromAction || fromKeyboard) ToggleSession();
        }

        public async void ToggleSession()
        {
            if (sessionManager == null)
            {
                Debug.LogError("[TelemetrySessionInput] Referência do TelemetrySessionManager ausente.");
                return;
            }
            if (sessionManager.IsSaving) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastToggleRealtime < minToggleInterval) return;
            lastToggleRealtime = now;

            if (sessionManager.IsRecording)
            {
                bool saved = await sessionManager.EndSessionAsync();
                if (this == null) return;
                Show(saved ? "Telemetria encerrada e salva." : "Falha ao salvar: tente novamente.",
                    saved ? stoppedClip : null);
            }
            else if (sessionManager.HasPendingSave)
            {
                bool saved = await sessionManager.RetryPendingSaveAsync();
                if (this == null) return;
                Show(saved ? "Dados pendentes salvos." : "Ainda não foi possível salvar.", null);
            }
            else
            {
                bool started = sessionManager.BeginSession();
                Show(started ? "Telemetria iniciada." : "Não foi possível iniciar a telemetria.",
                    started ? startedClip : null);
            }
        }

        private void UpdateStatus(string message) { if (statusText != null) statusText.text = message; }
        private void Show(string message, AudioClip sound)
        {
            if (statusText != null) statusText.text = message;
            if (feedbackAudio != null && sound != null) feedbackAudio.PlayOneShot(sound);
            Debug.Log("[TelemetrySessionInput] " + message);
        }
    }
}
