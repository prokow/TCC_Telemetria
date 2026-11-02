using System;

namespace MainProjeto.Telemetria.Participants
{
    public sealed class Participant
    {
        public string ParticipantCode { get; }
        public string DisplayName { get; }

        public Participant(string participantCode, string displayName)
        {
            if (!Guid.TryParseExact(participantCode, "N", out _))
                throw new ArgumentException("participant_code deve ser um GUID no formato N (32 caracteres).");
            // A PK é text: preservar o valor exato usado pelo banco, inclusive letras maiúsculas.
            ParticipantCode = participantCode;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "Participante " + participantCode.Substring(0, 8).ToUpperInvariant()
                : displayName.Trim();
        }
    }
}
