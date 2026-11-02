namespace MainProjeto.Core.Scripts.Model
{
    public static class QuestAdvanceRules
    {
        public static bool CanAdvance(int taskCount, int remaining, bool hasPendingTask,
            bool pendingBelongsToQuest, bool pendingAvailable, bool sequenceComplete, bool transitioning)
        {
            if (taskCount <= 0 || remaining < 0 || remaining > taskCount || sequenceComplete || transitioning) return false;
            if (!hasPendingTask) return remaining == 0;
            return pendingBelongsToQuest && (remaining == 0 || remaining == 1 && pendingAvailable);
        }
        public static bool CanStartNarrative(bool taskAvailable, bool autoComplete, bool lastTaskReady)
            => taskAvailable && (autoComplete || lastTaskReady);
    }

    // Evita rearmar o botao durante callbacks do proprio clique.
    public sealed class QuestAdvanceButtonState
    {
        public object QuestToken { get; private set; }
        public object TaskToken { get; private set; }
        public bool Ready { get; private set; }
        public bool Pressing { get; private set; }
        public bool Request(object quest, object task, bool allowed)
        {
            if (Pressing) return false;
            if (!allowed || quest == null) { Reset(); return false; }
            QuestToken = quest; TaskToken = task; Ready = true; return true;
        }
        public bool CanPress(object quest, bool allowed)
            => Ready && !Pressing && allowed && ReferenceEquals(QuestToken, quest);
        public bool TryBeginPress(object quest, bool allowed)
        {
            if (!CanPress(quest, allowed)) return false;
            Pressing = true; Ready = false; return true;
        }
        public void Reset() { QuestToken = null; TaskToken = null; Ready = false; Pressing = false; }
    }
}
