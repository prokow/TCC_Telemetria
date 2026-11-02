using System;
using MainProjeto.Telemetria.Participants;
using UnityEngine;

namespace MainProjeto.Telemetria.Sessions
{
    [DisallowMultipleComponent]
    public sealed class ApplicationSessionContext : MonoBehaviour
    {
        public static ApplicationSessionContext Instance { get; private set; }
        public ParticipantManager Participants { get; } = new ParticipantManager();
        public SessionContext Session { get; } = new SessionContext();
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        public static ApplicationSessionContext EnsureExists()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<ApplicationSessionContext>();
            return existing != null ? existing : new GameObject("ApplicationSessionContext").AddComponent<ApplicationSessionContext>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool TrySelect(Participant participant)
        {
            if (participant == null || !Session.CanChangeParticipant) return false;
            if (!Session.Prepare(participant)) return false;
            Participants.Select(participant);
            Changed?.Invoke();
            return true;
        }

        public bool TryClearSelection()
        {
            if (!Session.DiscardPreparation()) return false;
            Participants.Clear();
            Changed?.Invoke();
            return true;
        }

        public bool TryPrepareSession()
        {
            if (!Session.Prepare(Participants.Selected)) return false;
            Changed?.Invoke();
            return true;
        }

        public bool MarkActive(string sessionId)
        {
            if (!Session.MarkActive(sessionId)) return false;
            Changed?.Invoke();
            return true;
        }

        public void MarkPendingLocalSave(string sessionId)
        {
            if (Session.MarkPendingLocalSave(sessionId)) Changed?.Invoke();
        }

        public void MarkSavedLocally(string sessionId)
        {
            if (Session.MarkSavedLocally(sessionId)) Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
