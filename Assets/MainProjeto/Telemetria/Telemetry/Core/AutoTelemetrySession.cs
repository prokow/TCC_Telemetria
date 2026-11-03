using System.Collections;
using MainProjeto.Telemetria;
using MainProjeto.UI.Scripts;
using MainProjeto.Core.Scripts.Model;
using QuestTask = MainProject.Trindade.Core.Scripts.ScriptableObjects.Task;
using UnityEngine;

public class AutoTelemetrySession : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField]
    private TelemetrySessionManager sessionManager;

    [SerializeField] private QuestSystemModel questSystem;

    private void OnEnable()
    {
        if (questSystem == null) questSystem = FindFirstObjectByType<QuestSystemModel>();
        if (questSystem == null) return;
        questSystem.onQuestSequenceComplete.AddListener(OnExperienceCompleted);
        questSystem.onQuestLoaded.AddListener(OnQuestLoaded);
        questSystem.onQuestComplete.AddListener(OnQuestCompleted);
        questSystem.onTaskComplete.AddListener(OnTaskCompleted);
    }

    private void OnDisable()
    {
        if (questSystem == null) return;
        questSystem.onQuestSequenceComplete.RemoveListener(OnExperienceCompleted);
        questSystem.onQuestLoaded.RemoveListener(OnQuestLoaded);
        questSystem.onQuestComplete.RemoveListener(OnQuestCompleted);
        questSystem.onTaskComplete.RemoveListener(OnTaskCompleted);
    }

    [Header("Configurações")]
    [SerializeField]
    private bool resetMetricsOnStart = true;

    private IEnumerator Start()
    {
        // Aguarda um frame para inicializar os componentes.
        yield return null;

        if (sessionManager == null)
        {
            Debug.LogError(
                "[AutoTelemetrySession] SessionManager não atribuído."
            );
            yield break;
        }

        if (questSystem != null && questSystem.IsSequenceComplete) yield break;

        if (sessionManager.IsRecording ||
            sessionManager.HasPendingSave)
            yield break;

        if (resetMetricsOnStart &&
            SessionMetricsManager.Instance != null)
        {
            SessionMetricsManager.Instance.ResetMetrics();
        }

        if (sessionManager.BeginSession())
        {
            OnQuestLoaded();
            Debug.Log(
                "[AutoTelemetrySession] Telemetria iniciada automaticamente."
            );
        }
    }

    private void OnTaskCompleted(QuestTask task)
    {
        if (sessionManager != null && task != null)
            sessionManager.RecordEvent("quest_task_completed", task.name, task.description);
    }

    private void OnQuestLoaded()
    {
        if (sessionManager != null && questSystem != null && questSystem.quest != null)
            sessionManager.RecordEvent("quest_started", questSystem.quest.name, questSystem.quest.questTitle);
    }

    private void OnQuestCompleted()
    {
        if (sessionManager != null && questSystem != null && questSystem.quest != null)
            sessionManager.RecordEvent("quest_completed", questSystem.quest.name, questSystem.quest.questTitle);
    }

    private void OnExperienceCompleted()
    {
        if (sessionManager == null || !sessionManager.IsRecording) return;
        sessionManager.RecordEvent("experience_completed", "quest_sequence");
        FinishSession();
    }

    // Chamar quando o jogador terminar todas as fases.
    public async void FinishSession()
    {
        if (sessionManager == null ||
            !sessionManager.IsRecording)
            return;

        bool saved = await sessionManager.EndSessionAsync();

        Debug.Log(saved
            ? "[AutoTelemetrySession] Sessão finalizada e salva."
            : "[AutoTelemetrySession] Falha ao salvar a sessão.");
    }


}
