using System;
using MainProjeto.Telemetria.Participants;

namespace MainProjeto.Telemetria.Sessions
{
    public enum LocalSessionState { None, Prepared, Active, PendingLocalSave, SavedLocally }

    /// <summary>Identidade da execução local; não afirma que houve escrita no Supabase.</summary>
    public sealed class SessionContext
    {
        public string SessionId { get; private set; }
        public string ParticipantCode { get; private set; }
        public LocalSessionState State { get; private set; }
        public bool CanChangeParticipant => State != LocalSessionState.Active && State != LocalSessionState.PendingLocalSave;

        public bool Prepare(Participant participant)
        {
            if (participant == null || !CanChangeParticipant) return false;
            if (State == LocalSessionState.Prepared && ParticipantCode == participant.ParticipantCode) return true;
            SessionId = Guid.NewGuid().ToString("N");
            ParticipantCode = participant.ParticipantCode;
            State = LocalSessionState.Prepared;
            return true;
        }

        public bool DiscardPreparation()
        {
            if (!CanChangeParticipant) return false;
            SessionId = null;
            ParticipantCode = null;
            State = LocalSessionState.None;
            return true;
        }

        public bool MarkActive(string sessionId)
        {
            if (State != LocalSessionState.Prepared || SessionId != sessionId) return false;
            State = LocalSessionState.Active;
            return true;
        }

        public bool MarkPendingLocalSave(string sessionId)
        {
            if (State != LocalSessionState.Active || SessionId != sessionId) return false;
            State = LocalSessionState.PendingLocalSave;
            return true;
        }

        public bool MarkSavedLocally(string sessionId)
        {
            if (State != LocalSessionState.PendingLocalSave || SessionId != sessionId) return false;
            State = LocalSessionState.SavedLocally;
            return true;
        }
    }
}
