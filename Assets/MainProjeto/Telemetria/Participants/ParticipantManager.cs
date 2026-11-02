using System;

namespace MainProjeto.Telemetria.Participants
{
    public sealed class ParticipantManager
    {
        public Participant Selected { get; private set; }
        public event Action Changed;

        public void Select(Participant participant)
        {
            Selected = participant ?? throw new ArgumentNullException(nameof(participant));
            Changed?.Invoke();
        }

        public void Clear()
        {
            Selected = null;
            Changed?.Invoke();
        }
    }
}
