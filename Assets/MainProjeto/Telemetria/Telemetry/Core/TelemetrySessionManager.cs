using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MainProjeto.Telemetria.Sessions;
using UnityEngine;

namespace MainProjeto.Telemetria
{
    public class TelemetrySessionManager : MonoBehaviour
    {
        [Header("Gravadores")]
        [SerializeField] private TelemetryHeadRecorder headRecorder;
        [SerializeField] private TelemetryControllerRecorder controllerRecorder;
        [SerializeField] private bool finishOnApplicationPause;
        [Header("Gravacao local")]
        [Range(1f, 30f)] [SerializeField] private float checkpointIntervalSeconds = 15f;
        private string participantCode = string.Empty, participantDisplayName = string.Empty;
        private bool recording;
        private double startRealtime, nextCheckpoint;
        private DateTime startedUtc;
        private string currentSessionId, collectorUserId, collectionProjectOrigin, applicationVersion;
        private List<TelemetryEventFrame> events = new List<TelemetryEventFrame>();
        private int attempts, correctAnswers, errors, eventCount;
        private TelemetrySessionWriter writer;
        private TelemetrySessionPayload pendingBatch, finalMetadata;
        private Task<bool> checkpointTask, finalSaveTask;
        public bool IsRecording => recording;
        public bool IsSaving => finalSaveTask != null && !finalSaveTask.IsCompleted;
        public bool HasPendingSave => !recording && writer != null;
        public string ParticipantCode => participantCode;
        public string ParticipantDisplayName => participantDisplayName;
        public string CurrentSessionId => currentSessionId;
        public string LastStatus { get; private set; }
        public string StopReason { get; private set; }
        public event Action<string> StatusChanged;

        private void Awake()
        {
            var selected = ApplicationSessionContext.Instance?.Participants.Selected;
            if (selected != null) ConfigureParticipant(selected.ParticipantCode, selected.DisplayName);
            else if (ApplicationSessionContext.Instance == null) GenerateNewParticipant();
        }
        private async void Start()
        {
            try
            {
                var messages = await TelemetryLocalStorage.RecoverAsync(TelemetryLocalStorage.Root);
                if (this == null) return;
                foreach (string message in messages) Debug.Log("[Telemetry] " + message);
                Upload.TelemetrySyncController.NotifyLocalSave();
            }
            catch (Exception ex) { if (this != null) Debug.LogWarning("[Telemetry] Recuperacao pendente: " + ex.Message); }
        }
        private void SetStatus(string message)
        {
            LastStatus = message;
            if (this == null) return;
            Debug.Log("[Telemetry] " + message);
            StatusChanged?.Invoke(message);
        }
        public bool GenerateNewParticipant()
        {
            if (recording || writer != null) return false;
            participantCode = Guid.NewGuid().ToString("N");
            participantDisplayName = "Jogador-" + participantCode.Substring(0, 8).ToUpperInvariant();
            return true;
        }
        public void NewParticipantForNextSession() => GenerateNewParticipant();
        public bool ConfigureParticipant(string code, string displayName)
        {
            if (recording || writer != null || !Upload.TelemetryUploadPackage.ValidCode(code)) return false;
            participantCode = code.Trim(); participantDisplayName = (displayName ?? string.Empty).Trim();
            return true;
        }
        public bool BeginSession()
        {
            if (recording || writer != null || IsSaving) return false;
            var context = ApplicationSessionContext.Instance;
            if (context != null)
            {
                var selected = context.Participants.Selected;
                if (selected == null || !ConfigureParticipant(selected.ParticipantCode, selected.DisplayName) || !context.TryPrepareSession()) return false;
            }
            if (string.IsNullOrEmpty(participantCode)) GenerateNewParticipant();
            if (headRecorder == null || controllerRecorder == null || !headRecorder.CanRecord || !controllerRecorder.CanRecord) return false;
            currentSessionId = context != null ? context.Session.SessionId : Guid.NewGuid().ToString("N");
            startRealtime = Time.realtimeSinceStartupAsDouble;
            if (!headRecorder.Begin(startRealtime)) return false;
            if (!controllerRecorder.Begin(startRealtime)) { headRecorder.End(); return false; }
            if (context != null && !context.MarkActive(currentSessionId)) { headRecorder.End(); controllerRecorder.End(); return false; }
            writer = new TelemetrySessionWriter(TelemetryLocalStorage.GetSessionFolder(currentSessionId));
            collectorUserId = Upload.TelemetrySyncController.CurrentCollector;
            collectionProjectOrigin = Upload.TelemetrySyncController.CurrentProjectOrigin;
            applicationVersion = Application.version;
            startedUtc = DateTime.UtcNow;
            attempts = correctAnswers = errors = eventCount = 0;
            events = new List<TelemetryEventFrame>(); pendingBatch = finalMetadata = null;
            finalSaveTask = null; StopReason = null;
            recording = true;
            SetStatus("Telemetria iniciada.");
            StartCheckpoint(); // persist identity even before the first sample
            return true;
        }
        private TelemetrySessionPayload Metadata()
        {
            return new TelemetrySessionPayload {
                SessionId = currentSessionId, ParticipantCode = participantCode, ParticipantDisplayName = participantDisplayName,
                CollectorUserId = collectorUserId, SupabaseProjectOrigin = collectionProjectOrigin,
                StartedUtc = startedUtc.ToString("o", CultureInfo.InvariantCulture), EndedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ApplicationVersion = applicationVersion, DurationSeconds = (float)(Time.realtimeSinceStartupAsDouble - startRealtime),
                Attempts = attempts, CorrectAnswers = correctAnswers, Errors = errors
            };
        }
        private TelemetrySessionPayload TakeBatch()
        {
            var batch = Metadata();
            batch.HeadFrames = headRecorder.DrainHistory(); batch.ControllerFrames = controllerRecorder.DrainHistory();
            batch.Events = events; events = new List<TelemetryEventFrame>();
            return batch;
        }
        private async Task<bool> PersistBatchAsync()
        {
            if (pendingBatch == null) pendingBatch = TakeBatch();
            var batch = pendingBatch;
            var target = writer;
            try
            {
                await Task.Run(() => target.Append(batch));
                pendingBatch = null; batch = null; // samples are durable; release both references
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Falha no checkpoint; dados pendentes preservados. " + ex.Message);
                return false;
            }
        }
        private void StartCheckpoint()
        {
            nextCheckpoint = Time.realtimeSinceStartupAsDouble + Mathf.Clamp(checkpointIntervalSeconds, 1f, 30f);
            checkpointTask = PersistBatchAsync();
        }
        private void LateUpdate()
        {
            if (!recording) return;
            if (headRecorder.BufferFull || controllerRecorder.BufferFull || events.Count >= TelemetrySessionWriter.MaxBufferedEvents ||
                writer.StoredBytes >= TelemetrySessionWriter.StopAtBytes || eventCount >= TelemetrySessionWriter.MaxEvents - 1 ||
                (checkpointTask != null && checkpointTask.IsCompleted && !checkpointTask.Result))
            {
                StopForSafety("Limite de armazenamento ou falha de gravacao; a coleta foi interrompida.");
                return;
            }
            if ((checkpointTask == null || checkpointTask.IsCompleted) &&
                (Time.realtimeSinceStartupAsDouble >= nextCheckpoint || events.Count >= 64 ||
                 headRecorder.BufferedCount >= TelemetrySessionWriter.MaxBufferedFrames / 2 ||
                 controllerRecorder.BufferedCount >= TelemetrySessionWriter.MaxBufferedFrames / 2)) StartCheckpoint();
        }
        // For UnityEvent callers: true means finalization started, not that it is already saved.
        public bool EndSession()
        {
            if (!recording) return false;
            recording = false; headRecorder.End(); controllerRecorder.End();
            finalMetadata = Metadata();
            ApplicationSessionContext.Instance?.MarkPendingLocalSave(currentSessionId);
            SetStatus("Salvando telemetria...");
            finalSaveTask = FinalizeAsync();
            return true;
        }
        public Task<bool> EndSessionAsync()
        {
            if (recording) EndSession();
            return finalSaveTask ?? Task.FromResult(false);
        }
        public bool RetryPendingSave()
        {
            if (!HasPendingSave || IsSaving) return false;
            finalSaveTask = FinalizeAsync(); return true;
        }
        public Task<bool> RetryPendingSaveAsync()
        {
            RetryPendingSave(); return finalSaveTask ?? Task.FromResult(false);
        }
        private async Task<bool> FinalizeAsync()
        {
            try
            {
                if (checkpointTask != null) await checkpointTask;
                if (pendingBatch != null && !await PersistBatchAsync()) return false;
                // Flush everything collected while the previous checkpoint was being written.
                if (!await PersistBatchAsync()) return false;
                var target = writer; var metadata = finalMetadata;
                await Task.Run(() => target.Complete(metadata));
                target.Dispose(); writer = null;
                headRecorder.ReleaseHistory(); controllerRecorder.ReleaseHistory(); events = new List<TelemetryEventFrame>();
                pendingBatch = finalMetadata = null; checkpointTask = null;
                ApplicationSessionContext.Instance?.MarkSavedLocally(currentSessionId);
                Upload.TelemetrySyncController.NotifyLocalSave();
                SetStatus("Telemetria salva no dispositivo." + (string.IsNullOrEmpty(StopReason) ? "" : " " + StopReason));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Falha ao salvar; checkpoints e dados pendentes preservados. " + ex.Message);
                return false;
            }
        }
        private void StopForSafety(string reason)
        {
            if (!recording) return;
            StopReason = reason;
            // One reserved event explains why this session ended before the activity did.
            if (eventCount < TelemetrySessionWriter.MaxEvents)
            {
                events.Add(new TelemetryEventFrame { TimeSinceStart = (float)(Time.realtimeSinceStartupAsDouble - startRealtime), EventType = "collection_stopped", ObjectId = "application", Details = reason });
                eventCount++;
            }
            Debug.LogWarning("[Telemetry] " + reason);
            EndSession();
        }
        private static string Limited(string value, int length) => value == null ? "" : value.Length <= length ? value : value.Substring(0, length);
        public void RecordEvent(string eventType, string objectId, string details = "")
        {
            if (!recording || string.IsNullOrEmpty(eventType)) return;
            if (events.Count >= TelemetrySessionWriter.MaxBufferedEvents || eventCount >= TelemetrySessionWriter.MaxEvents - 1)
            {
                StopForSafety("Limite de eventos atingido; a coleta foi interrompida."); return;
            }
            events.Add(new TelemetryEventFrame { TimeSinceStart = (float)(Time.realtimeSinceStartupAsDouble - startRealtime), EventType = Limited(eventType, 120), ObjectId = Limited(objectId, 200), Details = Limited(details, 4000) });
            eventCount++;
        }
        public void RegisterAttempt(bool wasCorrect, string taskId)
        {
            if (!recording) return;
            if (events.Count >= TelemetrySessionWriter.MaxBufferedEvents || eventCount >= TelemetrySessionWriter.MaxEvents - 1) { StopForSafety("Limite de eventos atingido; a coleta foi interrompida."); return; }
            attempts++; if (wasCorrect) correctAnswers++; else errors++;
            RecordEvent(wasCorrect ? "task_correct" : "task_error", taskId);
        }
        private void OnApplicationPause(bool paused)
        {
            if (!recording) return;
            RecordEvent(paused ? "application_paused" : "application_resumed", "application");
            if (paused && finishOnApplicationPause) EndSession();
            else if (paused && recording && (checkpointTask == null || checkpointTask.IsCompleted)) StartCheckpoint();
        }
        private void OnApplicationQuit() { if (recording) EndSession(); }
        private async void OnDestroy()
        {
            if (recording) EndSession();
            var target = writer;
            if (finalSaveTask != null) await finalSaveTask;
            // A failed save must not prevent recovery after returning to the menu.
            target?.Dispose();
        }
    }
}