using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using MainProjeto.Telemetria;
using MainProjeto.UI.Scripts;

// Executa o AutoTelemetrySession real com eventos e gravador simulados.
// A suite ParticipantIntegrationChecks compila o mesmo script contra o Unity real.
internal static class Program
{
    private static int checks;
    private static readonly Quest A = new Quest { name = "A", questTitle = "Início" };
    private static readonly Quest B = new Quest { name = "B" };
    private static readonly Quest C = new Quest { name = "C" };
    private static readonly Quest D = new Quest { name = "D", questTitle = "Limite" };

    private static void Main()
    {
        var f = new Fixture(new List<Quest> { A, D });
        f.Start();
        f.Load(B);
        f.TaskComplete();
        Check(f.Manager.BeginCalls == 0 && f.Manager.Events.Count == 0, "antes do limite inicial não coleta");
        f.Load(A);
        Check(f.Manager.IsRecording && f.Manager.BeginCalls == 1, "quest inicial abre sessão");
        Check(SessionMetricsManager.Instance.Resets == 1, "métricas zeradas somente no início aceito");
        f.Load(A);
        Check(f.Manager.Events.Count(e => e == "quest_started:A") == 1, "notificação repetida não duplica início");
        f.TaskComplete();
        f.Model.onQuestComplete.Invoke();
        Check(f.Manager.IsRecording && f.Manager.Events.Contains("quest_completed:A"), "concluir quest não encerra coleta");
        f.Load(B);
        f.Load(C);
        Check(f.Manager.BeginCalls == 1 && f.Manager.EndCalls == 0, "quests não listadas mantêm a mesma sessão");
        Check(f.Manager.Events.Contains("quest_started:B") && f.Manager.Events.Contains("quest_started:C"), "intermediárias entram na coleta");
        f.Load(D);
        Check(!f.Manager.IsRecording && f.Manager.EndCalls == 1, "carregar limite encerra imediatamente");
        Check(f.Manager.Events.Last() == "telemetry_interval_completed:D", "fim identifica quest limite");
        Check(!f.Manager.Events.Contains("quest_started:D"), "quest limite é exclusiva");
        int events = f.Manager.Events.Count;
        f.TaskComplete();
        f.Model.onQuestComplete.Invoke();
        f.Load(A);
        f.Model.onQuestSequenceComplete.Invoke();
        Check(f.Manager.BeginCalls == 1 && f.Manager.EndCalls == 1 && f.Manager.Events.Count == events, "encerramento impede reinício e eventos posteriores");

        var loadedFirst = new Fixture(new List<Quest> { A, B, D });
        loadedFirst.Load(A); // QuestSystem.Start antes de AutoTelemetrySession.Start.
        loadedFirst.Start();
        Check(loadedFirst.Manager.BeginCalls == 1, "primeira quest já carregada é capturada");
        loadedFirst.Load(B);
        Check(loadedFirst.Manager.IsRecording, "item intermediário não encerra a sessão");
        loadedFirst.Model.IsSequenceComplete = true;
        loadedFirst.Model.onQuestSequenceComplete.Invoke();
        Check(loadedFirst.Manager.EndCalls == 1 && loadedFirst.Manager.Events.Last() == "experience_completed:quest_sequence", "fim da experiência salva sem alcançar limite");

        var late = new Fixture(new List<Quest> { A, D });
        late.Model.IsSequenceComplete = true;
        late.Load(A);
        late.Start();
        Check(late.Manager.BeginCalls == 0, "experiência concluída não inicia coleta");

        var stopped = new Fixture(new List<Quest> { A, D });
        stopped.Start();
        stopped.Load(A);
        stopped.Manager.IsRecording = false; // Parada de proteção/manual.
        stopped.Load(A);
        stopped.Load(B);
        stopped.Load(A);
        Check(stopped.Manager.BeginCalls == 1, "parada externa não reinicia sessão");

        var failed = new Fixture(new List<Quest> { A, D });
        failed.Manager.AllowBegin = false;
        failed.Start();
        failed.Load(A);
        failed.Load(B);
        failed.Load(D);
        Check(failed.Manager.Events.Count == 0 && failed.Manager.EndCalls == 0, "início recusado não fabrica sessão nem eventos");
        Check(SessionMetricsManager.Instance.Resets == 0, "início recusado preserva métricas");

        var reversed = new Fixture(new List<Quest> { A, D });
        reversed.Start();
        reversed.Load(D);
        reversed.Load(A);
        Check(reversed.Manager.BeginCalls == 0, "limite já ultrapassado impede início tardio");

        foreach (var invalid in new List<Quest>[] { null, new List<Quest>(), new List<Quest> { A },
                     new List<Quest> { A, A }, new List<Quest> { null, D }, new List<Quest> { A, null, D } })
        {
            var bad = new Fixture(invalid);
            bad.Start();
            bad.Load(A);
            Check(bad.Manager.BeginCalls == 0, "configuração inválida não inicia coleta");
        }

        var lifecycle = new Fixture(new List<Quest> { A, D });
        lifecycle.Start();
        lifecycle.Load(A);
        Invoke(lifecycle.Auto, "OnDisable");
        lifecycle.Load(B);
        Check(!lifecycle.Manager.Events.Contains("quest_started:B"), "desativar remove listeners");
        Invoke(lifecycle.Auto, "OnEnable");
        Check(lifecycle.Manager.Events.Count(e => e == "quest_started:B") == 1, "reativar sincroniza quest atual");
        lifecycle.Load(D);
        Check(lifecycle.Manager.EndCalls == 1, "reativação preserva limite e sessão única");
        Console.WriteLine("PASS: " + checks + " verificações do AutoTelemetrySession real com dependências simuladas.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
    }
    private static void Invoke(object target, string name)
        => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

    private sealed class Fixture
    {
        public readonly AutoTelemetrySession Auto = new AutoTelemetrySession();
        public readonly TelemetrySessionManager Manager = new TelemetrySessionManager();
        public readonly QuestSystemModel Model = new QuestSystemModel();
        public Fixture(List<Quest> quests)
        {
            SessionMetricsManager.Instance = new SessionMetricsManager();
            Set(Auto, "sessionManager", Manager);
            Set(Auto, "questSystem", Model);
            Set(Auto, "telemetryQuestSequence", quests);
            Invoke(Auto, "OnEnable");
        }
        public void Start() => Invoke(Auto, "Start");
        public void Load(Quest quest) { Model.quest = quest; Model.onQuestLoaded.Invoke(); }
        public void TaskComplete() => Model.onTaskComplete.Invoke(new MainProject.Trindade.Core.Scripts.ScriptableObjects.Task { name = "task" });
    }
}

namespace UnityEngine
{
    public class MonoBehaviour { protected static T FindFirstObjectByType<T>() where T : class => null; }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public sealed class SerializeField : Attribute { }
    public static class Debug
    {
        public static void Log(string text) { }
        public static void LogWarning(string text) { }
        public static void LogError(string text) { }
    }
}
namespace MainProjeto.Core.Scripts.ScriptableObjects
{
    public class Quest { public string name; public string questTitle; }
}
namespace MainProject.Trindade.Core.Scripts.ScriptableObjects
{
    public class Task { public string name; public string description; }
}
namespace MainProjeto.Core.Scripts.Model
{
    public class Signal
    {
        private event Action Listeners;
        public void AddListener(Action listener) => Listeners += listener;
        public void RemoveListener(Action listener) => Listeners -= listener;
        public void Invoke() => Listeners?.Invoke();
    }
    public class Signal<T>
    {
        private event Action<T> Listeners;
        public void AddListener(Action<T> listener) => Listeners += listener;
        public void RemoveListener(Action<T> listener) => Listeners -= listener;
        public void Invoke(T value) => Listeners?.Invoke(value);
    }
    public class QuestSystemModel
    {
        public Quest quest;
        public bool IsSequenceComplete;
        public readonly Signal onQuestLoaded = new Signal(), onQuestComplete = new Signal(), onQuestSequenceComplete = new Signal();
        public readonly Signal<MainProject.Trindade.Core.Scripts.ScriptableObjects.Task> onTaskComplete = new Signal<MainProject.Trindade.Core.Scripts.ScriptableObjects.Task>();
    }
}
namespace MainProjeto.UI.Scripts
{
    public class SessionMetricsManager
    {
        public static SessionMetricsManager Instance;
        public int Resets;
        public void ResetMetrics() => Resets++;
    }
}
namespace MainProjeto.Telemetria
{
    public class TelemetrySessionManager
    {
        public bool IsRecording, AllowBegin = true;
        public int BeginCalls, EndCalls;
        public readonly List<string> Events = new List<string>();
        public bool BeginSession()
        {
            BeginCalls++;
            if (IsRecording || !AllowBegin) return false;
            return IsRecording = true;
        }
        public void RecordEvent(string type, string id, string details = "")
        {
            if (IsRecording) Events.Add(type + ":" + id);
        }
        public System.Threading.Tasks.Task<bool> EndSessionAsync()
        {
            EndCalls++;
            IsRecording = false;
            return System.Threading.Tasks.Task.FromResult(true);
        }
    }
}
