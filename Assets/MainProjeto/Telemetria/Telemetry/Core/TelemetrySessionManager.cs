using System;
using System.Collections.Generic;
using System.Globalization;
using MainProjeto.Telemetria.Sessions;
using UnityEngine;

namespace MainProjeto.Telemetria
{
    public class TelemetrySessionManager : MonoBehaviour
    {
        [Header("Gravadores")]
        [SerializeField] private TelemetryHeadRecorder headRecorder;
        [SerializeField] private TelemetryControllerRecorder controllerRecorder;
        [Tooltip("Encerrar ao suspender o app. Desativado para continuar a mesma sessão até a última quest.")]
        [SerializeField] private bool finishOnApplicationPause;
        // Identificação criada automaticamente: não exige campo de nome na VR.
        // O GUID completo identifica o participante; Jogador-XXXXXXXX é um apelido visual.
        private string participantCode = string.Empty;
        private string participantDisplayName = string.Empty;

        private bool recording;
        private double startRealtime;
        private DateTime startedUtc;
        private string currentSessionId;
        private readonly List<TelemetryEventFrame> events = new List<TelemetryEventFrame>();
        private int attempts, correctAnswers, errors;
        private TelemetrySessionPayload pendingPayload; // retém dados se falhar o salvamento
        private string collectorUserId, collectionProjectOrigin;

        public bool IsRecording { get { return recording; } }
        public string ParticipantCode { get { return participantCode; } }
        public string ParticipantDisplayName { get { return participantDisplayName; } }

        // Novo participante anônimo por execução do TelemetrySessionManager.
        // Para mais de uma pessoa no mesmo headset, use NewParticipantForNextSession()
        // ENTRE sessões. O código real é o GUID inteiro; o apelido é apenas visual.
        private void Awake()
        {
            var selected = ApplicationSessionContext.Instance?.Participants.Selected;
            if (selected != null) ConfigureParticipant(selected.ParticipantCode, selected.DisplayName);
            else if (ApplicationSessionContext.Instance == null) GenerateNewParticipant();
        }

        public bool GenerateNewParticipant()
        {
            if (recording || pendingPayload != null)
            {
                Debug.LogWarning("[TelemetrySessionManager] Encerre e salve a sessão antes de trocar de participante.");
                return false;
            }

            string id = Guid.NewGuid().ToString("N");
            participantCode = id;
            participantDisplayName = "Jogador-" + id.Substring(0, 8).ToUpperInvariant();
            Debug.Log("[TelemetrySessionManager] Participante anônimo: " + participantDisplayName);
            return true;
        }

        // Método void pode ser usado em um Button.OnClick() ou chamado por script.
        public void NewParticipantForNextSession()
        {
            GenerateNewParticipant();
        }

        // Identifica o participante antes do início de uma sessão.
        // O código deve ser único; o nome de exibição é opcional.
        public bool ConfigureParticipant(string code, string displayName)
        {
            if (recording || pendingPayload != null)
            {
                Debug.LogWarning("[TelemetrySessionManager] Identifique o participante antes de iniciar a sessão.");
                return false;
            }
            if (!Upload.TelemetryUploadPackage.ValidCode(code))
            {
                Debug.LogWarning("[TelemetrySessionManager] Código do participante não pode estar vazio.");
                return false;
            }
            participantCode = code.Trim();
            participantDisplayName = (displayName ?? string.Empty).Trim();
            return true;
        }
        public string CurrentSessionId { get { return currentSessionId; } }
        public bool HasPendingSave { get { return pendingPayload != null; } }

        public bool BeginSession()
        {
            // O fluxo do menu sempre exige a identidade selecionada; abrir a cena isoladamente
            // mantém o modo de teste anônimo existente.
            var context = ApplicationSessionContext.Instance;
            if (context != null)
            {
                var selected = context.Participants.Selected;
                if (selected == null || !ConfigureParticipant(selected.ParticipantCode, selected.DisplayName) ||
                    !context.TryPrepareSession()) return false;
            }
            // Segurança extra: garante código e nome mesmo após alteração do ciclo de vida.
            if (string.IsNullOrEmpty(participantCode))
                GenerateNewParticipant();

            if (recording || pendingPayload != null)
            {
                Debug.LogWarning("[TelemetrySessionManager] Gravação ativa ou salvamento pendente.");
                return false;
            }
            if (headRecorder == null || controllerRecorder == null ||
                !headRecorder.CanRecord || !controllerRecorder.CanRecord)
            {
                Debug.LogError("[TelemetrySessionManager] Configure os dois gravadores e seus Transforms.");
                return false;
            }

            string newId = context != null ? context.Session.SessionId : Guid.NewGuid().ToString("N");
            double now = Time.realtimeSinceStartupAsDouble;
            DateTime utcNow = DateTime.UtcNow;

            if (!headRecorder.Begin(now)) return false;
            if (!controllerRecorder.Begin(now))
            {
                headRecorder.End();
                return false;
            }

            if (context != null && !context.MarkActive(newId))
            {
                headRecorder.End();
                controllerRecorder.End();
                return false;
            }

            currentSessionId = newId;
            collectorUserId = Upload.TelemetrySyncController.CurrentCollector;
            collectionProjectOrigin = Upload.TelemetrySyncController.CurrentProjectOrigin;
            startRealtime = now;
            startedUtc = utcNow;
            attempts = correctAnswers = errors = 0;
            events.Clear();
            recording = true;
            // Se precisar notificar outros sistemas, exponha evento daqui,
            // mas não deixe o salvamento depender da ordem dos listeners.
            Debug.Log("[TelemetrySessionManager] Sessão iniciada: " + newId);
            return true;
        }

        public bool EndSession()
        {
            if (!recording) return false;

            // Primeiro interrompe ambos; depois cria uma cópia imutável dos dados.
            headRecorder.End();
            controllerRecorder.End();
            recording = false;
            ApplicationSessionContext.Instance?.MarkPendingLocalSave(currentSessionId);
            double now = Time.realtimeSinceStartupAsDouble;

            pendingPayload = new TelemetrySessionPayload
            {
                SessionId = currentSessionId,
                ParticipantCode = participantCode,
                ParticipantDisplayName = participantDisplayName,
                CollectorUserId = collectorUserId,
                SupabaseProjectOrigin = collectionProjectOrigin,
                StartedUtc = startedUtc.ToString("o", CultureInfo.InvariantCulture),
                EndedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ApplicationVersion = Application.version,
                DurationSeconds = (float)(now - startRealtime),
                Attempts = attempts,
                CorrectAnswers = correctAnswers,
                Errors = errors,
                HeadFrames = headRecorder.CopyHistory(),
                ControllerFrames = controllerRecorder.CopyHistory(),
                Events = new List<TelemetryEventFrame>(events)
            };
            return RetryPendingSave();
        }

        public bool RetryPendingSave()
        {
            if (pendingPayload == null) return false;
            try
            {
                TelemetryLocalStorage.Save(pendingPayload);
                Upload.TelemetrySyncController.NotifyLocalSave();
                ApplicationSessionContext.Instance?.MarkSavedLocally(pendingPayload.SessionId);
                Debug.Log("[TelemetrySessionManager] Sessão salva: " + pendingPayload.SessionId);
                pendingPayload = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[TelemetrySessionManager] Falha ao salvar. Dados preservados em memória: " + ex);
                return false;
            }
        }

        // Ex.: RecordEvent("circuit_error_detected", "R2", "resistor em paralelo")
        public void RecordEvent(string eventType, string objectId, string details = "")
        {
            if (!recording || string.IsNullOrEmpty(eventType)) return;
            events.Add(new TelemetryEventFrame
            {
                TimeSinceStart = (float)(Time.realtimeSinceStartupAsDouble - startRealtime),
                EventType = eventType, ObjectId = objectId, Details = details
            });
        }

        public void RegisterAttempt(bool wasCorrect, string taskId)
        {
            if (!recording) return;
            attempts++;
            if (wasCorrect) correctAnswers++;
            else errors++;
            RecordEvent(wasCorrect ? "task_correct" : "task_error", taskId);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!recording) return;
            RecordEvent(paused ? "application_paused" : "application_resumed", "application");
            if (paused && finishOnApplicationPause) EndSession();
        }
    }
}
