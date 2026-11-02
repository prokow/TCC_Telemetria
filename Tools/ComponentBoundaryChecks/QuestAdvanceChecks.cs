using System;
using System.IO;
using System.Linq;
using MainProjeto.Core.Scripts.Model;
using Newtonsoft.Json.Linq;

internal static class QuestAdvanceChecks
{
    private static int checks;
    private static void Check(bool condition, string description)
    { checks++; if (!condition) throw new Exception(description); }
    public static int Run()
    {
        checks = 0;
        Check(!QuestAdvanceRules.CanAdvance(0,0,false,false,false,false,false), "Quest vazia nao libera avanco.");
        Check(!QuestAdvanceRules.CanAdvance(3,2,true,true,true,false,false), "Uma task disponivel nao e suficiente enquanto outra esta pendente.");
        Check(!QuestAdvanceRules.CanAdvance(3,1,true,false,true,false,false), "Tarefa de outra quest nao pode liberar o botao.");
        Check(!QuestAdvanceRules.CanAdvance(3,1,true,true,false,false,false), "Ultima task com dependencia bloqueada nao pode liberar.");
        Check(QuestAdvanceRules.CanAdvance(3,1,true,true,true,false,false), "A ultima task pode ser entregue ao botao apos a narrativa.");
        Check(!QuestAdvanceRules.CanAdvance(3,1,false,false,false,false,false), "Evento sem task nao pode pular a ultima tarefa.");
        Check(QuestAdvanceRules.CanAdvance(3,0,false,false,false,false,false), "Quest totalmente concluida permite avancar.");
        Check(!QuestAdvanceRules.CanAdvance(3,0,false,false,false,false,true), "Transicao em andamento rejeita repeticoes.");
        Check(!QuestAdvanceRules.CanAdvance(3,0,false,false,false,true,false), "Experiencia concluida nao libera nova quest.");
        Check(!QuestAdvanceRules.CanStartNarrative(false,true,false), "Auto Concluir nao pode ignorar dependencias da fala inicial.");
        Check(!QuestAdvanceRules.CanStartNarrative(true,false,false), "Narrativa final espera todas as demais tarefas.");
        Check(QuestAdvanceRules.CanStartNarrative(true,true,false), "Fala inicial pode concluir sua propria tarefa.");
        Check(QuestAdvanceRules.CanStartNarrative(true,false,true), "Fala final inicia quando a ultima task esta disponivel.");

        var gate = new QuestAdvanceButtonState(); var firstQuest = new object(); var nextQuest = new object(); var last = new object();
        Check(!gate.Ready && !gate.TryBeginPress(firstQuest,true), "Botao nasce bloqueado.");
        Check(!gate.Request(firstQuest,last,false), "Pedido antecipado nao arma o botao.");
        Check(gate.Request(firstQuest,last,true), "Narrativa terminada pode armar o botao.");
        Check(!gate.CanPress(nextQuest,true), "Pedido antigo nao vale para a proxima quest.");
        Check(gate.TryBeginPress(firstQuest,true), "Primeiro clique e aceito.");
        Check(!gate.Request(firstQuest,null,true), "onQuestComplete reentrante nao rearma durante o clique.");
        Check(!gate.TryBeginPress(firstQuest,true), "Mesmo clique nao pode iniciar outra transicao.");
        gate.Reset();
        Check(!gate.Ready && !gate.CanPress(nextQuest,true), "Trocar de quest limpa o estado visual/funcional.");
        Check(gate.Request(nextQuest,last,true), "Nova fase pode liberar seu proprio botao.");
        gate.Reset();
        Check(!gate.Request(null,last,true), "Sem quest ativa nao ha liberacao.");

        string path = Environment.GetEnvironmentVariable("CIRCUITOS_VR_QUEST_REPORT");
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) CheckActualQuestGraphs(JObject.Parse(File.ReadAllText(path)));
        return checks;
    }

    private static void CheckActualQuestGraphs(JObject report)
    {
        foreach (var quest in report["quests"])
        {
            var tasks = quest["taskGraph"] as JArray;
            if (tasks == null) continue;
            Check(tasks.Count > 0 && tasks.Count < 20, "Grafo real precisa ter tarefas validas.");
            var ids = tasks.Select(t => (string)t["id"]).ToArray();
            var dependencies = tasks.Select(t => t["dependencies"].Select(d => Array.IndexOf(ids,(string)d)).ToArray()).ToArray();
            for (int mask = 0; mask < (1 << tasks.Count); mask++)
            {
                int remaining = Enumerable.Range(0,tasks.Count).Count(i => (mask & (1 << i)) == 0);
                bool allDone = remaining == 0;
                Check(QuestAdvanceRules.CanAdvance(tasks.Count,remaining,false,false,false,false,false) == allDone,
                    "Evento sem tarefa so libera com todas concluidas: " + quest["quest"]);
                for (int candidate = 0; candidate < tasks.Count; candidate++)
                {
                    bool available = (mask & (1 << candidate)) == 0 && dependencies[candidate].All(i => i >= 0 && (mask & (1 << i)) != 0);
                    bool expected = allDone || remaining == 1 && available;
                    Check(QuestAdvanceRules.CanAdvance(tasks.Count,remaining,true,true,available,false,false) == expected,
                        "Liberacao inconsistente nas combinacoes do grafo real: " + quest["quest"]);
                }
            }
        }
    }
}
