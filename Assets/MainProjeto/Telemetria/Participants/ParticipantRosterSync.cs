using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MainProjeto.Telemetria.Participants
{
    public sealed class ParticipantRosterUpdate
    {
        public IReadOnlyList<Participant> Participants { get; }
        public bool Changed { get; }
        public ParticipantRosterUpdate(IReadOnlyList<Participant> participants, bool changed)
        {
            Participants = participants;
            Changed = changed;
        }
    }

    /// <summary>Consulta somente o repositório; não conhece frontend, Canvas ou cenas.</summary>
    public sealed class ParticipantRosterSync
    {
        private readonly ParticipantRepository repository;
        private bool hasSnapshot;
        public IReadOnlyList<Participant> Snapshot { get; private set; } = Array.Empty<Participant>();

        public ParticipantRosterSync(ParticipantRepository repository)
            => this.repository = repository ?? throw new ArgumentNullException(nameof(repository));

        public async Task<ParticipantRosterUpdate> RefreshAsync(CancellationToken cancellationToken = default)
        {
            var participants = await repository.GetAllAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            bool changed = !hasSnapshot || !SameParticipants(Snapshot, participants);
            Snapshot = participants;
            hasSnapshot = true;
            return new ParticipantRosterUpdate(Snapshot, changed);
        }

        public void Clear()
        {
            Snapshot = Array.Empty<Participant>();
            hasSnapshot = false;
        }

        private static bool SameParticipants(IReadOnlyList<Participant> previous, IReadOnlyList<Participant> current)
        {
            if (previous.Count != current.Count) return false;
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var participant in previous) names.Add(participant.ParticipantCode, participant.DisplayName);
            foreach (var participant in current)
                if (!names.TryGetValue(participant.ParticipantCode, out string name) || name != participant.DisplayName) return false;
            return true;
        }
    }
}
