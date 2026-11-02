#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using MainProjeto.Scripts.ParentCoponent;
using MainProjeto.Scripts.Protoboard_Interactor;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using QuestTask = MainProject.Trindade.Core.Scripts.ScriptableObjects.Task;
using Object = UnityEngine.Object;

namespace MainProjeto.EditorChecks
{
    // Executor fixo, offline, sem mudar a cena aberta nem entrar/sair do Play Mode.
    public static class SampleSceneInteractionChecks
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string OutputFolder = "Temp/SampleSceneChecks";
        private static readonly List<string> failures = new List<string>();
        private static readonly List<string> warnings = new List<string>();
        private static readonly List<object> quests = new List<object>();
        private static int checks;
        private static GameObject fixture;
        private static Scene fixtureScene;
        private static XRInteractionManager manager;
        private static bool running;

        [MenuItem("Tools/Circuitos VR/Testar SampleScene sem Play Mode")]
        public static void Run()
        {
            if (running) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Saia do Play Mode para executar a auditoria isolada da SampleScene."); return; }
            running = true; checks = 0; failures.Clear(); warnings.Clear(); quests.Clear();
            Application.logMessageReceived += CaptureEngineErrors;
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.OpenPreviewScene(ScenePath);
                var components = preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c != null).ToArray();
                Case("Referencias da cena", () => AuditScene(preview, components));
                Case("Quests reais", () => CheckQuests(components.OfType<QuestSystemModel>().Single()));
                fixtureScene = EditorSceneManager.NewPreviewScene();
                fixture = new GameObject("SampleSceneChecks_Fixture");
                SceneManager.MoveGameObjectToScene(fixture, fixtureScene);
                manager = Child("XRManager").AddComponent<XRInteractionManager>();
                Message(manager, "Awake");
                Case("Encaixe em coordenadas mundiais", CheckWorldAttach);
                Case("Alvos com varios colliders", CheckCompoundTargets);
                Case("Liberacao das celulas ocupadas", CheckOccupiedCells);
                Case("Botao de proxima quest e callback reentrante", CheckNextQuestButton);
                Case("Retorno, queda e protecao da protoboard", CheckBoundary);
            }
            catch (Exception error) { failures.Add("Executor: " + Unwrap(error).GetType().Name + ": " + Unwrap(error).Message); }
            finally
            {
                if (fixtureScene.IsValid()) EditorSceneManager.ClosePreviewScene(fixtureScene);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                Application.logMessageReceived -= CaptureEngineErrors;
                fixture = null; manager = null; running = false;
                Directory.CreateDirectory(OutputFolder);
                File.WriteAllText(OutputFolder + "/editor-report.json", JsonConvert.SerializeObject(new {
                    utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                    mode = "Unity Editor: preview scenes + real XR API; no Play Mode/physics stepping/headset/network",
                    checks, failures = failures.ToArray(), warnings = warnings.ToArray(), quests = quests.ToArray()
                }, Formatting.Indented));
                Debug.Log($"SAMPLE_SCENE_CHECKS: {(failures.Count == 0 ? "PASS" : "FAIL")} {checks} verificacoes, {failures.Count} falhas, {warnings.Count} avisos. Relatorio: {OutputFolder}/editor-report.json");
            }
        }

        private static Exception Unwrap(Exception error) => error is TargetInvocationException target && target.InnerException != null ? target.InnerException : error;
        private static void CaptureEngineErrors(string condition, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && failures.Count < 100)
                failures.Add("Unity: " + condition);
        }
        private static void Case(string name, Action test)
        { try { test(); } catch (Exception error) { failures.Add(name + ": " + Unwrap(error).GetType().Name + ": " + Unwrap(error).Message); } }
        private static void Check(bool value, string description)
        { checks++; if (!value) failures.Add(description); }
        private static IEnumerable<GameObject> AllObjects(Scene scene)
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject);
        private static string PathOf(Transform transform)
            => transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;

        private static void AuditScene(Scene scene, MonoBehaviour[] components)
        {
            foreach (var go in AllObjects(scene))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) == 0, "Script ausente: " + PathOf(go.transform));
            Check(components.OfType<QuestSystemModel>().Count() == 1, "Deve haver exatamente um QuestSystemModel.");
            Check(components.OfType<XRInteractionManager>().Any(), "XRInteractionManager ausente na cena.");
            var pieces = components.OfType<EletronicComponent>().ToArray();
            var boards = components.OfType<ProtoboardInteractor>().ToArray();
            Check(pieces.Length > 0 && boards.Length > 0, "Pecas/protoboard nao encontradas.");
            int withoutArea = 0, autoConnect = 0;
            foreach (var piece in pieces)
            {
                string path = PathOf(piece.transform);
                Check(piece.GetComponent<Rigidbody>() != null, "Rigidbody ausente: " + path);
                Check(piece.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger), "Collider fisico ausente: " + path);
                var serialized = new SerializedObject(piece);
                var area = serialized.FindProperty("movementArea")?.objectReferenceValue as BoxCollider;
                if (area == null) withoutArea++;
                else Check(!area.transform.IsChildOf(piece.transform) && area.isTrigger, "Caixa de limite invalida: " + path);
                if (serialized.FindProperty("autoConnectOnAwake").boolValue) autoConnect++;
                var explicitManager = piece.interactionManager;
                Check(explicitManager == null || explicitManager.gameObject.scene == scene, "Peca referencia manager de outra cena: " + path);
            }
            foreach (var board in boards)
            {
                Check(board.GetComponent<Grid>() != null, "Grid ausente: " + PathOf(board.transform));
                Check(board.GetComponentsInChildren<Collider>(true).Any(c => c.isTrigger), "Trigger ausente na protoboard: " + PathOf(board.transform));
            }
            warnings.Add($"Inventario: {pieces.Length} pecas, {boards.Length} protoboards, {autoConnect} pecas com autoconexao.");
            if (withoutArea > 0) warnings.Add($"{withoutArea}/{pieces.Length} pecas sem movementArea: retorno/queda por limite esta desativado nelas.");
            var model = components.OfType<QuestSystemModel>().Single();
            var data = new SerializedObject(model);
            var sequence = data.FindProperty("sequenceOfQuests");
            var circuits = data.FindProperty("sceneCircuits");
            Check(sequence.arraySize == circuits.arraySize, "Quantidade de circuitos difere da sequencia de quests.");
            for (int i = 0; i < circuits.arraySize; i++)
                Check(circuits.GetArrayElementAtIndex(i).objectReferenceValue != null, "Circuito ausente na fase " + i);
            for (int i = 0; i < model.onQuestComplete.GetPersistentEventCount(); i++)
            {
                var target = model.onQuestComplete.GetPersistentTarget(i);
                string method = model.onQuestComplete.GetPersistentMethodName(i);
                Check(target != null && target.GetType().GetMethod(method, Type.EmptyTypes) != null, "Callback de conclusao invalido: " + method);
            }
        }

        private static void CheckQuests(QuestSystemModel sceneModel)
        {
            var sequenceProperty = new SerializedObject(sceneModel).FindProperty("sequenceOfQuests");
            var sequence = Enumerable.Range(0, sequenceProperty.arraySize).Select(i => sequenceProperty.GetArrayElementAtIndex(i).objectReferenceValue as Quest).ToList();
            Check(sequence.Count == 7 && sequence.All(q => q != null), "A sequencia deve resolver as sete quests reais.");
            var go = new GameObject("QuestModel_Fixture");
            SceneManager.MoveGameObjectToScene(go, sceneModel.gameObject.scene);
            var model = go.AddComponent<QuestSystemModel>();
            model.onTaskComplete = new UnityEvent<QuestTask>(); model.onQuestLoaded = new UnityEvent();
            model.onQuestComplete = new UnityEvent(); model.onQuestSequenceComplete = new UnityEvent();
            Set(model, "sequenceOfQuests", sequence);
            int taskEvents = 0, questEvents = 0, finalEvents = 0;
            model.onTaskComplete.AddListener(_ => taskEvents++);
            model.onQuestComplete.AddListener(() => questEvents++);
            model.onQuestSequenceComplete.AddListener(() => finalEvents++);
            try
            {
                for (int index = 0; index < sequence.Count; index++)
                {
                    var quest = sequence[index];
                    if (quest == null) continue;
                    Set(model, "_questIndex", index); model.LoadNewQuest(quest);
                    Check(quest.tasks.Count > 0 && quest.tasks.All(t => t != null), "Quest sem tarefas validas: " + quest.name);
                    Check(quest.tasks.Distinct().Count() == quest.tasks.Count, "Tarefa duplicada: " + quest.name);
                    foreach (var task in quest.tasks.Where(t => t != null))
                        foreach (var dependency in task.dependencies ?? Array.Empty<QuestTask>())
                            Check(dependency != null && quest.tasks.Contains(dependency), "Dependencia fora da quest: " + quest.name + "/" + task.name);
                    var blocked = quest.tasks.FirstOrDefault(t => t != null && !model.IsTaskAvailable(t));
                    if (blocked != null) { model.CompleteTask(blocked); Check(!model.IsTaskDone(blocked), "Dependencia permitiu conclusao antecipada: " + quest.name); }
                    int before = taskEvents;
                    bool observedDone = true;
                    Check(!model.CanAdvanceAfterTask(null), "Evento generico liberou quest incompleta: " + quest.name);
                    foreach (var task in quest.tasks.Where(t => t != null))
                    {
                        var captured = task;
                        model.SubscribeToTaskComplete(() => { observedDone &= model.IsTaskDone(captured); }, captured);
                    }
                    int attempts = 0;
                    while (quest.tasks.Any(t => t != null && !model.IsTaskDone(t)) && attempts++ <= quest.tasks.Count)
                    {
                        var available = quest.tasks.FirstOrDefault(t => t != null && model.IsTaskAvailable(t));
                        if (available == null) break;
                        int remaining = quest.tasks.Count(t => !model.IsTaskDone(t));
                        Check(model.CanAdvanceAfterTask(available) == (remaining == 1), "Botao liberado antes da ultima task: " + quest.name);
                        model.CompleteTask(available); model.CompleteTask(available);
                    }
                    Check(quest.tasks.All(model.IsTaskDone), "Dependencia circular ou quest impossivel de concluir: " + quest.name);
                    Check(taskEvents - before == quest.tasks.Count, "Conclusao duplicada ou tarefa perdida: " + quest.name);
                    Check(observedDone, "Callbacks receberam tarefa ainda marcada como incompleta: " + quest.name);
                    int completed = questEvents; model.CheckAllTasks(); model.CheckAllTasks();
                    Check(questEvents == completed && questEvents == index + 1, "Evento de quest repetido ou ausente: " + quest.name);
                    Check(finalEvents == (index == sequence.Count - 1 ? 1 : 0), "Fim da experiencia ocorreu fora da ultima quest: " + quest.name);
                    quests.Add(new { quest = quest.name, title = quest.questTitle, tasks = quest.tasks.Count, complete = quest.tasks.All(model.IsTaskDone) });
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static GameObject Child(string name)
        { var go = new GameObject(name); go.transform.SetParent(fixture.transform, false); return go; }
        private static void Message(object target, string name, params object[] arguments)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (method == null) continue;
                method.Invoke(target, arguments); return;
            }
            throw new MissingMethodException(name);
        }
        private static void Set(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value); return;
            }
            throw new MissingFieldException(name);
        }
        private static void CheckNextQuestButton()
        {
            var previousModel = QuestSystemModel.Instance;
            var modelObject = Child("NextQuestModel_Fixture");
            var model = modelObject.AddComponent<QuestSystemModel>();
            var quest = ScriptableObject.CreateInstance<Quest>();
            var first = ScriptableObject.CreateInstance<QuestTask>(); first.name = "first";
            var last = ScriptableObject.CreateInstance<QuestTask>(); last.name = "last"; last.dependencies = new[] { first };
            quest.tasks = new List<QuestTask> { first,last };
            var instanceProperty = typeof(QuestSystemModel).GetProperty("Instance",BindingFlags.Static|BindingFlags.Public);
            NextQuestButton button = null;
            try
            {
                instanceProperty.SetValue(null,model);
                model.onTaskComplete = new UnityEvent<QuestTask>(); model.onQuestComplete = new UnityEvent(); model.onQuestLoaded = new UnityEvent();
                Set(model,"sequenceOfQuests",new List<Quest>{quest}); model.LoadNewQuest(quest);
                var go = Child("NextQuestButton_Fixture"); button = go.AddComponent<NextQuestButton>();
                // Fixture de logica: nao instanciar materiais de Outline com ciclo de vida de Play Mode.
                Set(go.GetComponent<QuickOutline.Scripts.Outline>(),"renderers",Array.Empty<Renderer>());
                button.interactionManager = manager; Message(button,"Awake"); Set(button,"_animator",null); Message(button,"Start");
                manager.RegisterInteractable((IXRInteractable)button);
                button.TurnOnButton(first); Check(!button.IsReadyToAdvance,"Botao aceitou task que nao e a ultima pendente.");
                model.CompleteTask(first); button.TurnOnButton(last);
                Check(button.IsReadyToAdvance,"Ultima tarefa valida nao liberou o botao apos entrega da narrativa.");
                Set(model,"transitionInProgress",true);
                Check(!button.IsReadyToAdvance && model.CanPrepareFinalNarrative(last),"Transicao deve bloquear clique sem perder narrativa final pronta.");
                Set(model,"transitionInProgress",false);
                model.LoadNewQuest(quest); Check(!button.IsReadyToAdvance,"Carregar quest conservou botao liberado.");
                model.CompleteTask(first); button.TurnOnButton(last);
                bool rearmed = false;
                model.onTaskComplete.AddListener(task => { if(task == last){ button.TurnOnButton(); rearmed = button.IsReadyToAdvance; } });
                var hand = Child("NextQuestSelector").AddComponent<SampleSceneCheckInteractor>(); hand.Pressed = true;
                hand.interactionManager = manager; Message(hand,"Awake"); manager.RegisterInteractor((IXRInteractor)hand);
                manager.SelectEnterUnconditionally(hand,button);
                Check(model.IsTaskDone(last) && model.IsSequenceComplete,"Clique final nao concluiu a ultima task/experiencia.");
                Check(!rearmed && !button.IsReadyToAdvance,"Callback de conclusao rearmou o botao durante o clique.");
            }
            finally
            {
                if(button != null) manager.UnregisterInteractable((IXRInteractable)button);
                instanceProperty.SetValue(null,previousModel);
                Object.DestroyImmediate(modelObject); Object.DestroyImmediate(quest); Object.DestroyImmediate(first); Object.DestroyImmediate(last);
            }
        }
        private static ProtoboardInteractor Board(string name)
        {
            var go = Child(name); go.AddComponent<Grid>().cellSize = Vector3.one * 0.02f;
            go.AddComponent<BoxCollider>().isTrigger = true;
            var board = go.AddComponent<ProtoboardInteractor>(); board.interactionManager = manager;
            Message(board, "Awake"); manager.RegisterInteractor((IXRInteractor)board); return board;
        }
        private static EletronicComponent Piece(string name)
        {
            var go = Child(name); go.AddComponent<Rigidbody>().useGravity = false; go.AddComponent<BoxCollider>().size = Vector3.one * 0.01f;
            var piece = go.AddComponent<EletronicComponent>(); piece.interactionManager = manager;
            Message(piece, "Awake"); manager.RegisterInteractable((IXRInteractable)piece); return piece;
        }
        private static void CheckWorldAttach()
        {
            var board = Board("OffsetBoard"); board.transform.SetPositionAndRotation(new Vector3(12,2,-7), Quaternion.Euler(0,0,37));
            board.transform.localScale = new Vector3(1.5f,0.7f,2);
            var piece = Piece("OffsetPiece");
            var parent = Child("DifferentParent"); parent.transform.position = new Vector3(40,10,-15); piece.transform.SetParent(parent.transform, true);
            var grid = board.GetComponent<Grid>(); var cell = new Vector3Int(-2,-4,0);
            piece.transform.position = grid.GetCellCenterWorld(cell);
            Check(Vector3.Distance(board.GetAttachTransform(piece).position, grid.GetCellCenterWorld(cell)) < 0.0001f,
                "GetAttachTransform usa localPosition como coordenada mundial: encaixe falha com pais deslocados.");
        }
        private static void CheckCompoundTargets()
        {
            var board = Board("CompoundBoard"); var piece = Piece("CompoundPiece");
            var one = piece.GetComponent<Collider>(); var two = piece.gameObject.AddComponent<SphereCollider>();
            Message(board, "OnTriggerEnter", one); Message(board, "OnTriggerEnter", two);
            var targets = new List<IXRInteractable>(); board.GetValidTargets(targets);
            Check(targets.Count(t => ReferenceEquals(t, piece)) == 1, "Dois colliders duplicaram o alvo.");
            Message(board, "OnTriggerExit", one); board.GetValidTargets(targets);
            Check(targets.Contains(piece), "Sair com um collider removeu a peca ainda sobreposta por outro.");
            Message(board, "OnTriggerExit", two); board.GetValidTargets(targets);
            Check(!targets.Contains(piece), "Alvo nao saiu apos o ultimo collider.");
            Message(board, "OnTriggerEnter", one); Object.DestroyImmediate(piece.gameObject); board.GetValidTargets(targets);
            Check(targets.All(t => t is Object obj && obj != null), "Alvo destruido ficou na lista da protoboard.");
        }
        private static void CheckOccupiedCells()
        {
            var board = Board("OccupiedBoard"); var piece = Piece("MovedPiece");
            var grid = board.GetComponent<Grid>(); var origin = grid.GetCellCenterWorld(new Vector3Int(-2,-4,0));
            piece.transform.position = origin; manager.SelectEnterUnconditionally(board, piece);
            Check(board.IsSelecting(piece), "Selecao pela protoboard falhou na fixture.");
            piece.transform.position = grid.GetCellCenterWorld(new Vector3Int(2,-4,0));
            manager.SelectCancel((IXRSelectInteractor)board, (IXRSelectInteractable)piece);
            var next = Piece("NextPiece"); next.transform.position = origin;
            Check(board.CanSelect((IXRSelectInteractable)next), "Celula original ficou ocupada depois que a peca saiu/mudou de posicao.");
        }
        private static void CheckBoundary()
        {
            var piece = Piece("BoundedPiece"); var body = piece.GetComponent<Rigidbody>();
            var area = Child("Area").AddComponent<BoxCollider>(); area.isTrigger = true; area.size = Vector3.one * 2;
            Set(piece, "movementArea", area); piece.transform.position = Vector3.zero; Message(piece, "LateUpdate");
            var hand = Child("SelectInteractor").AddComponent<SampleSceneCheckInteractor>();
            hand.interactionManager = manager; Message(hand, "Awake"); manager.RegisterInteractor((IXRInteractor)hand);
            hand.Pressed = true; manager.SelectEnterUnconditionally(hand, piece);
            Check(piece.isSelected, "Selecao XR da fixture falhou.");
            piece.transform.position = Vector3.right * 3; Message(piece, "LateUpdate");
            piece.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Late);
            Check(!piece.isSelected && piece.transform.position.magnitude < 0.0001f, "Retorno nao cancelou grab/restaurou posicao.");
            Check(!piece.IsSelectableBy((IXRSelectInteractor)hand), "Grip mantido pressionado pode reagarrar imediatamente.");
            hand.Pressed = false; Check(piece.IsSelectableBy((IXRSelectInteractor)hand), "Soltar grip nao desbloqueou a peca.");
            Set(piece, "outsideAreaAction", OutsideComponentAreaAction.ReleaseAndFall);
            Message(piece, "LateUpdate"); hand.Pressed = true; manager.SelectEnterUnconditionally(hand, piece);
            piece.transform.position = Vector3.right * 3; Message(piece, "LateUpdate");
            piece.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Late);
            Check(!piece.isSelected && body.useGravity && !body.isKinematic, "Queda nao liberou Rigidbody/gravidade.");
            body.linearVelocity = Vector3.down; Message(piece, "LateUpdate");
            Check(body.linearVelocity.y < -0.9f, "Limite esta interrompendo a queda a cada frame.");
            piece.transform.position = Vector3.zero; Message(piece, "LateUpdate");
            var board = Board("BoundaryBoard"); board.transform.position = Vector3.right * 8;
            piece.transform.position = board.GetComponent<Grid>().GetCellCenterWorld(new Vector3Int(-2,-4,0));
            manager.SelectEnterUnconditionally(board, piece); var connectedPosition = piece.transform.position;
            Message(piece, "LateUpdate");
            Check(board.IsSelecting(piece) && piece.transform.position == connectedPosition, "Limite desfez uma conexao da protoboard fora da caixa.");
            manager.SelectCancel((IXRSelectInteractor)board, (IXRSelectInteractable)piece);
        }
    }

    public sealed class SampleSceneCheckInteractor : XRBaseInteractor
    {
        public bool Pressed;
        public override bool isSelectActive => Pressed;
        public override void GetValidTargets(List<IXRInteractable> targets) => targets.Clear();
    }
}
#endif
