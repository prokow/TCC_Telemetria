namespace MainProjeto.Core.Scripts.Model
{
    public enum QuestCompletionResult { None, QuestCompleted, SequenceCompleted }

    /// <summary>Conclusão única por quest e por experiência, independente da interface Unity.</summary>
    public sealed class QuestCompletionProgress
    {
        private bool questNotified;
        public bool IsSequenceComplete { get; private set; }
        public void LoadQuest() => questNotified = false;

        public QuestCompletionResult Complete(int taskCount, bool allTasksDone, bool isFinalQuest)
        {
            if (IsSequenceComplete || questNotified || taskCount <= 0 || !allTasksDone) return QuestCompletionResult.None;
            questNotified = true;
            if (!isFinalQuest) return QuestCompletionResult.QuestCompleted;
            IsSequenceComplete = true;
            return QuestCompletionResult.SequenceCompleted;
        }
    }
}
