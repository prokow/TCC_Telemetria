using System;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.ScriptableObjects;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MainProjeto.Core.Scripts.Model
{
    public class QuestSystemModel : MonoBehaviour
    {
        public static QuestSystemModel Instance { get; private set; }
        
        public Quest quest;
    
        // tarefas ativas da quest
        private List<LoadedTask> _runtimeTasks = new List<LoadedTask>();

        // eventos a serem usando para tais tarefas
        public UnityEvent<Task> onTaskComplete;  
        public UnityEvent onQuestComplete;
        public UnityEvent onQuestLoaded;
        public UnityEvent onQuestSequenceComplete = new UnityEvent();
        private readonly QuestCompletionProgress completionProgress = new QuestCompletionProgress();
        public bool IsSequenceComplete => completionProgress.IsSequenceComplete;
        private bool transitionInProgress;
        public bool IsTransitionInProgress => transitionInProgress;
        public bool AreAllCurrentTasksDone => quest != null && _runtimeTasks.Count > 0 && _runtimeTasks.TrueForAll(t => t.IsDone);

        // A narrativa so pode entregar ao botao a ultima tarefa pendente.
        public bool CanAdvanceAfterTask(Task pendingTask) => EvaluateAdvance(pendingTask, transitionInProgress);
        public bool CanPrepareFinalNarrative(Task pendingTask) => pendingTask != null && EvaluateAdvance(pendingTask, false);
        private bool EvaluateAdvance(Task pendingTask, bool transitioning)
        {
            if (quest == null) return false;
            int remaining = 0;
            foreach (var task in _runtimeTasks) if (!task.IsDone) remaining++;
            return QuestAdvanceRules.CanAdvance(_runtimeTasks.Count, remaining,
                pendingTask != null, GetTask(pendingTask) != null, pendingTask != null && IsTaskAvailable(pendingTask),
                IsSequenceComplete, transitioning);
        }
        

        [Header("Modo de Debug")]
        public bool debugMode = false;
        
        // sequência de quests a serem seguidos 
        [Header("Quest's Sequence to Follow")]
        [SerializeField] 
        private List<Quest> sequenceOfQuests;
        private int _questIndex = 0;

        
        [Header("Transição de Fase (VR Fade)")]
        [SerializeField] private Image fadeScreen; 
        [SerializeField] private float fadeDuration = 1.0f;

        [Header("Prefab's a se ligar ou desligar (circuitos)")] 
        [SerializeField]
        private List<GameObject> sceneCircuits = new List<GameObject>();


        [Header("Ordem das Fases a ser aleatório ou não")] 
        [SerializeField]
        private bool randomQuestOrder = false;

        [Header("Número de Quest's iniciais que não serão sorteadas (ex: mantém as 2 primeiras")]
        [SerializeField] private int fixedStartingQuests = 2;
        private List<int> _phaseOrder = new List<int>();
        
        // Esta struct substitui os booleanos individuais (lacreAberto, pinoColetado, pinoSolto)
        private class LoadedTask
        {
            public Task TaskData;
            private bool _isDone;

            public bool IsDone
            {
                get { return _isDone; }
                set
                {
                    if (_isDone == value) return;
                    _isDone = value;
                    if (value)
                    {
                        OnComplete?.Invoke();   
                    }
                }
            }
            public List<LoadedTask> DependsOn = new();  // dependencia de cada tarefa
            public Action OnComplete;                   // ação de tarefa completa
        }
        
        
        // inicializa a instância única do sistema de Quest (Singleton)
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            { 
                Instance = this;
                // O modelo referencia circuitos desta cena. Uma nova experiência precisa
                // de um modelo novo, com índice e referências próprios da SampleScene.
            }
        }

        private void OnEnable()
        {
            //MainProject.Trindade.Core.Scripts.Quest_UI.QuestUIPresenter.OnQuestCompleteFinished += AdvanceToNexQuest;
        }
        
        private void OnDisable()
        {
            //MainProject.Trindade.Core.Scripts.Quest_UI.QuestUIPresenter.OnQuestCompleteFinished -= AdvanceToNexQuest;
        }

        private void Start()
        {

            for (int i = 0; i < sequenceOfQuests.Count; i++)
            {
                _phaseOrder.Add(i);
            }

            if (randomQuestOrder)
            {
                int startIndex = Mathf.Clamp(fixedStartingQuests, 0, _phaseOrder.Count);

                for (int i = startIndex; i < _phaseOrder.Count; i++)
                {
                    int temp = _phaseOrder[i];
                    
                    int randomIndex = UnityEngine.Random.Range(i, _phaseOrder.Count);
                    
                    _phaseOrder[i] = _phaseOrder[randomIndex];
                    _phaseOrder[randomIndex] = temp;
                }
            }
            
            StartNextQuest();
        }

        // --------------- Debug -------------------------
        private void Update()
        {
            if (debugMode && Keyboard.current != null)
            {
                if (Keyboard.current.digit0Key.wasPressedThisFrame)
                {
                    SkipQuest();
                }

                if (Keyboard.current.digit1Key.wasPressedThisFrame)
                {
                    SkipTask();
                }
            }
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void SkipQuest()
        {
            foreach (var task in _runtimeTasks)
            {
                if (IsTaskAvailable(task.TaskData))
                {
                    CompleteTask(task.TaskData);
                }
            }
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void SkipTask()
        {
            foreach (var task in _runtimeTasks)
            {
                if (IsTaskAvailable(task.TaskData))
                {
                    CompleteTask(task.TaskData);
                    Debug.LogWarning($"<color=magenta> Skipped Task: {task.TaskData.name}");
                    return;
                }
            }
        }
        
        // --------------------------------------------------
        

        // função para fazer o subscriber na tarefa completa
        public void SubscribeToTaskComplete(Action onComplete, Task task)
        {
            var loadedTask = GetTask(task);
        
            if (loadedTask != null)
            {
                loadedTask.OnComplete += onComplete;    
            }
            else
            {
                Debug.LogWarning("A tarefa -- " + task.name + "-- nao foi encontrado");   
            }
        }

        public void UnsubscribeFromTaskComplete(Action onComplete, Task task)
        {
            var loadedTask = GetTask(task);

            if (loadedTask != null)
            {
                loadedTask.OnComplete -= onComplete;
            }
        }
        
        

        // função para carregar nova Quest quando começar a interação/terminar a interação
        public void LoadNewQuest(Quest newQuest)
        {
            if (newQuest == null)
            {
                Debug.LogWarning("No Quest Loaded ");
                return; 
            }
        
            quest = newQuest;
            completionProgress.LoadQuest();
            Debug.Log($"<color=cyan>Quest Carregada -> {newQuest.name}</color>");
            _runtimeTasks.Clear();

            // para carregar as novas tasks
            foreach (Task task in quest.tasks)
            {
                if(task == null) continue;
            
                _runtimeTasks.Add(new LoadedTask { TaskData = task, IsDone = false});
            }

            // para carregar as dependencias de determinada task, caso for nulo será adicionado a lista
            foreach (LoadedTask task in _runtimeTasks)
            {
                if (task.TaskData.dependencies == null) continue;
            
                foreach (Task taskDependency in task.TaskData.dependencies)
                {
                    LoadedTask foundLoadedDependency = GetTask(taskDependency);   
                    if (foundLoadedDependency == null)
                        continue;

                    task.DependsOn.Add(foundLoadedDependency);
                }
            }
            
            onQuestLoaded?.Invoke();
        }
        
        
    
        // função auxiliar para fazer o get da task na própria lista
        private LoadedTask GetTask(Task taskToCheck)
        {
            for (int i = 0; i < _runtimeTasks.Count; i++)
            {
                // se a função a ser checada for encontrada no runtimeTasks, será retornada a própria a ser usada
                if (_runtimeTasks[i].TaskData == taskToCheck)
                {
                    return _runtimeTasks[i];
                }
            }
            return null;
        }

        // função para checar se a task está disponivel
        public bool IsTaskAvailable(Task taskToCheck)
        {
            var loadedTask = GetTask(taskToCheck);
            if (loadedTask == null || loadedTask.IsDone) return false;

            foreach (var dep in loadedTask.DependsOn)
            {
                if (!dep.IsDone)
                    return false;
            }

            return true;
        }

        // função para checa se a tarefa foi concluida
        public bool IsTaskDone(Task taskToCheck)
        {
            var loadedTask = GetTask(taskToCheck);
            return loadedTask != null && loadedTask.IsDone;
        }
    
    
        //Setter da Tarefa Cumprida
        // ReSharper disable Unity.PerformanceAnalysis
        public void CompleteTask(Task task)
        {
            LoadedTask loadedTask = GetTask(task);
            if (loadedTask == null || loadedTask.IsDone)
                return;

            // caso encontre alguma dependencia que nao esteja pronta, para imediatamente
            foreach (LoadedTask dep in loadedTask.DependsOn)
            {
                if (!dep.IsDone)
                    return;
            }
            loadedTask.IsDone = true;
            Debug.LogWarning($"<color=green>{loadedTask.TaskData.description}</color>");
            onTaskComplete?.Invoke(task);
            CheckAllTasks();
        }
    
    
        // função para verificar se todas as tarefas foram finalizadas
        public void CheckAllTasks()
        {
            if (quest == null) return;
            bool isFinalQuest = sequenceOfQuests != null && sequenceOfQuests.Count > 0 &&
                                _questIndex == sequenceOfQuests.Count - 1 && sequenceOfQuests[_questIndex] == quest;
            var completion = completionProgress.Complete(_runtimeTasks.Count, _runtimeTasks.TrueForAll(t => t.IsDone), isFinalQuest);
            if (completion != QuestCompletionResult.None)
            {
                onQuestComplete?.Invoke();
                Debug.Log($"<color=white>Interação finalizada da Quest {quest.questTitle}!</color>");
                // A última tarefa (incluindo a narrativa final) já terminou; não exigir
                // um clique posterior no botão de transição para finalizar a experiência.
                if (completion == QuestCompletionResult.SequenceCompleted) onQuestSequenceComplete?.Invoke();
                //StartCoroutine(NextQuest());
            }
        }
        
        // partirá para a próxima quest, caso não houver mais quest's retornará o debug
        private void AdvanceToNexQuest()
        {
            _questIndex++;
            if(_questIndex < sequenceOfQuests.Count)
                StartNextQuest();
            else
                Debug.Log("All the quest's have been completed");
        }
        
        
        private void ActivateCurrentCircuit()
        {
            for (int i = 0; i < sceneCircuits.Count; i++)
            {
                if (sceneCircuits[i] != null)
                {
                    bool isCurrentLevel = (i == _questIndex);
                    sceneCircuits[i].SetActive(isCurrentLevel);
                }
            }
        }

        // caso a sequência não seja nula e tenha mais de uma sequencia de Quests maior que o questIndex
        // será carregado a próxima Quest
        // ReSharper disable Unity.PerformanceAnalysis
        private void StartNextQuest()
        {
            if (sequenceOfQuests == null || _questIndex >= sequenceOfQuests.Count)
            {
                Debug.LogWarning("Quest Sequence null or out of range");
                return;
            }
            
            LoadNewQuest(sequenceOfQuests[_questIndex]);
            
            ActivateCurrentCircuit();
            
            DebugQuest();
        }
        
        public void TriggerNextQuestTransition()
        {
            if (transitionInProgress || IsSequenceComplete || !AreAllCurrentTasksDone) return;
            transitionInProgress = true;
            StartCoroutine(TransitionToNextLevelRoutine());
        }

        private System.Collections.IEnumerator TransitionToNextLevelRoutine()
        {
            // 1. Escurecer a tela (Fade Out)
            if (fadeScreen != null) yield return StartCoroutine(FadeRoutine(0f, 2f));

            // 2. Tela totalmente preta - Avança para a próxima Quest
            AdvanceToNexQuest();

            // Breve pausa para conforto no escuro
            yield return new WaitForSeconds(0.5f);

            // 3. Clarear a tela (Fade In)
            if (fadeScreen != null) yield return StartCoroutine(FadeRoutine(2f, 0f));
            transitionInProgress = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }


        // inicia a quest com debug mostrando as tarefas
        private void DebugQuest()
        {
            if (quest != null)
            {
                Debug.Log($"<color=white>{quest.questTitle}</color>");
            }

            foreach (Task t in quest.tasks)
            {
                //GameObject prefab = Instantiate(prefabTask, prefabContainer);
                Debug.LogWarning($"<color=yellow>{t.description}</color>");
            }
        }
        
        private System.Collections.IEnumerator FadeRoutine(float startAlpha, float endAlpha)
        {
            float elapsedTime = 0f;
            Color color = fadeScreen.color;
            color.a = startAlpha;
            fadeScreen.color = color;

            while (elapsedTime < fadeDuration)
            {
                elapsedTime += Time.deltaTime;
                color.a = Mathf.Lerp(startAlpha, endAlpha, elapsedTime / fadeDuration);
                fadeScreen.color = color;
                yield return null;
            }

            color.a = endAlpha;
            fadeScreen.color = color;
        }
    }
}
