using System.Collections.Generic;
using MainProjeto.Telemetria;
using MainProjeto.UI.Scripts;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using QuestTask = MainProject.Trindade.Core.Scripts.ScriptableObjects.Task;
using UnityEngine;

public class AutoTelemetrySession : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField]
    private TelemetrySessionManager sessionManager;

    [SerializeField] private QuestSystemModel questSystem;

    [Header("Quests que delimitam a coleta")]
    [Tooltip("Uma única sessão: começa ao carregar a primeira quest e termina ao carregar a última (exclusiva). " +
             "Use pelo menos duas quests diferentes. As intermediárias não dividem nem interrompem a coleta. " +
             "Lista vazia desativa o início automático.")]
    [SerializeField] private List<Quest> telemetryQuestSequence = new List<Quest>();

    private bool initialized;
    private bool sessionStarted;
    private bool intervalFinished;
    private Quest startQuest;
    private Quest endQuest;
    private Quest lastRecordedQuest;

    private void OnEnable()
    {
        if (questSystem == null) questSystem = FindFirstObjectByType<QuestSystemModel>();
        if (questSystem == null) return;
        questSystem.onQuestSequenceComplete.AddListener(OnExperienceCompleted);
        questSystem.onQuestLoaded.AddListener(OnQuestLoaded);
        questSystem.onQuestComplete.AddListener(OnQuestCompleted);
        questSystem.onTaskComplete.AddListener(OnTaskCompleted);
        if (initialized) OnQuestLoaded();
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

    private void Start()
    {
        if (sessionManager == null || questSystem == null)
        {
            Debug.LogError("[AutoTelemetrySession] Atribua SessionManager e QuestSystem.");
            return;
        }

        if (telemetryQuestSequence == null || telemetryQuestSequence.Count < 2)
        {
            Debug.LogWarning("[AutoTelemetrySession] Configure pelo menos duas quests na lista: " +
                             "primeira = início da coleta; última = encerramento. Coleta automática desativada.");
            return;
        }
        foreach (Quest configuredQuest in telemetryQuestSequence)
        {
            if (configuredQuest != null) continue;
            Debug.LogError("[AutoTelemetrySession] A lista de quests da telemetria contém uma referência vazia.");
            return;
        }

        startQuest = telemetryQuestSequence[0];
        endQuest = telemetryQuestSequence[telemetryQuestSequence.Count - 1];
        if (startQuest == endQuest)
        {
            Debug.LogError("[AutoTelemetrySession] As quests de início e encerramento devem ser diferentes.");
            return;
        }

        // Todos os Awake já ocorreram. Se o Start do modelo carregou a primeira
        // quest antes deste Start, processá-la aqui; caso contrário, aguardar o evento.
        initialized = true;
        OnQuestLoaded();
    }

    private void OnTaskCompleted(QuestTask task)
    {
        if (sessionStarted && !intervalFinished && sessionManager != null && task != null)
            sessionManager.RecordEvent("quest_task_completed", task.name, task.description);
    }

    private void OnQuestLoaded()
    {
        if (!initialized || intervalFinished || sessionManager == null || questSystem == null) return;
        if (questSystem.IsSequenceComplete) { OnExperienceCompleted(); return; }
        Quest currentQuest = questSystem.quest;
        if (currentQuest == null) return;

        // A quest limite não entra na coleta: apenas registra o motivo do fim.
        if (currentQuest == endQuest)
        {
            intervalFinished = true;
            if (sessionStarted && sessionManager.IsRecording)
            {
                sessionManager.RecordEvent("telemetry_interval_completed", currentQuest.name, currentQuest.questTitle);
                FinishSession();
            }
            return;
        }

        if (!sessionStarted)
        {
            if (currentQuest != startQuest) return;
            if (!sessionManager.BeginSession())
            {
                Debug.LogWarning("[AutoTelemetrySession] Não foi possível iniciar a telemetria na quest configurada.");
                return;
            }
            sessionStarted = true;
            if (resetMetricsOnStart && SessionMetricsManager.Instance != null)
                SessionMetricsManager.Instance.ResetMetrics();
            Debug.Log("[AutoTelemetrySession] Telemetria iniciada na quest " + currentQuest.name + ".");
        }

        // Uma parada manual ou de proteção não deve abrir outra sessão neste intervalo.
        if (!sessionManager.IsRecording) { intervalFinished = true; return; }
        if (lastRecordedQuest == currentQuest) return;
        lastRecordedQuest = currentQuest;
        sessionManager.RecordEvent("quest_started", currentQuest.name, currentQuest.questTitle);
    }

    private void OnQuestCompleted()
    {
        if (sessionStarted && !intervalFinished && sessionManager != null && questSystem != null && questSystem.quest != null)
            sessionManager.RecordEvent("quest_completed", questSystem.quest.name, questSystem.quest.questTitle);
    }

    private void OnExperienceCompleted()
    {
        intervalFinished = true;
        if (!sessionStarted || sessionManager == null || !sessionManager.IsRecording) return;
        sessionManager.RecordEvent("experience_completed", "quest_sequence");
        FinishSession();
    }

    // Também encerra se a experiência terminar antes de alcançar a quest limite.
    public async void FinishSession()
    {
        intervalFinished = true;
        if (sessionManager == null ||
            !sessionManager.IsRecording)
            return;

        bool saved = await sessionManager.EndSessionAsync();

        Debug.Log(saved
            ? "[AutoTelemetrySession] Sessão finalizada e salva."
            : "[AutoTelemetrySession] Falha ao salvar a sessão.");
    }


}
