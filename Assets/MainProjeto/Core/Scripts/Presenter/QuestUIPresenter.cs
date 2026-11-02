using System;
using System.Collections;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.View;
using UnityEngine;

namespace MainProject.Trindade.Core.Scripts.Quest_UI
{
    public class QuestUIPresenter : MonoBehaviour
    {
        public enum DisplayMode
        {
            OneTask, //  aparece uma task por cada de quest
            AllTasks, //  aparece todas as task da quest
        }

        [Header("Display Mode for Quest")] [SerializeField]
        private DisplayMode displayMode = DisplayMode.OneTask;

        // tempo a ser usado no fade de uma task para outra
        private readonly float _fadeDuration = 0.5f;
        private readonly float _readDelay = 1.5f;


        // Lista de tarefas a serem spawnados no canvas UI
        private readonly List<TaskUIView> _uiTasks = new List<TaskUIView>();
        private int _currentTask = 0;

        private readonly Queue<IEnumerator> _eventQueue = new Queue<IEnumerator>();
        private bool _isTransitioning = false;

        public static event Action OnQuestCompleteFinished;

        private QuestUIView _view;

        private void Awake()
        {
            _view = GetComponent<QuestUIView>();
            if (_view == null) Debug.LogWarning("QuestUIPresenter is missing QuestUIView");
        }

        private void OnEnable()
        {
            if (_view != null) _view.TogglePanels(false);

            StartCoroutine(WaitingQuestSystem());

        }

        private IEnumerator WaitingQuestSystem()
        {
            while (QuestSystemModel.Instance == null)
            {
                yield return null;
            }

            QuestSystemModel.Instance.onQuestLoaded.RemoveListener(OnQuestLoaded_Event);
            QuestSystemModel.Instance.onTaskComplete.RemoveListener(OnTaskComplete_Event);
            QuestSystemModel.Instance.onQuestComplete.RemoveListener(OnQuestComplete_Event);

            QuestSystemModel.Instance.onQuestLoaded.AddListener(OnQuestLoaded_Event);
            QuestSystemModel.Instance.onTaskComplete.AddListener(OnTaskComplete_Event);
            QuestSystemModel.Instance.onQuestComplete.AddListener(OnQuestComplete_Event);

            if (QuestSystemModel.Instance.quest != null)
            {
                _eventQueue.Clear();
                _isTransitioning = false;
                OnQuestLoaded_Event();
            }

        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void OnQuestLoaded_Event()
        {
            EnqueueEvent(Routine_RefreshUI(), "Carregar Nova Quest");
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void OnTaskComplete_Event(Task task)
        {
            EnqueueEvent(Routine_TaskUpdated(task), $"Próxima Task: {task.description}");
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void OnQuestComplete_Event()
        {
            EnqueueEvent(Routine_QuestComplete(), "Quest Completa");
        }


        private void EnqueueEvent(IEnumerator eventRoutine, string eventName)
        {
            if (_isTransitioning)
            {
                _eventQueue.Enqueue(eventRoutine);
                Debug.Log(
                    $"<color=yellow>[Fila - ESPERA]</color> O evento <b>'{eventName}'</b> entrou na fila. Tamanho atual: {_eventQueue.Count}");
            }
            else
            {
                StartCoroutine(ProcessQueue(eventRoutine));
            }
        }

        private IEnumerator ProcessQueue(IEnumerator firstRoutine)
        {
            _isTransitioning = true;

            yield return StartCoroutine(firstRoutine);

            while (_eventQueue.Count > 0)
            {
                Debug.Log(
                    $"<color=orange>[Fila - PROCESSANDO]</color> Puxando o próximo evento da fila. Restam aguardando: {_eventQueue.Count - 1}");
                IEnumerator nextRoutine = _eventQueue.Dequeue();
                yield return StartCoroutine(nextRoutine);
            }

            Debug.Log("<color=cyan>[Fila - VAZIA]</color> Todos os eventos concluídos. Semáforo aberto.");
            _isTransitioning = false;
        }

        // UI é atualizada de acordo com a quest for completa
        public IEnumerator Routine_RefreshUI()
        {
            _uiTasks.Clear();
            _view.ClearContainer();

            if (QuestSystemModel.Instance.quest != null)
            {
                _view.SetTitle(QuestSystemModel.Instance.quest.questTitle);
                foreach (Task taskData in QuestSystemModel.Instance.quest.tasks)
                {
                    TaskUIView newTaskUIView = _view.CreateTaskUI(taskData);
                    _uiTasks.Add(newTaskUIView);
                }

                if (displayMode == DisplayMode.AllTasks)
                {
                    yield return StartCoroutine(Routine_ShowAllTasks());
                }
                else if (displayMode == DisplayMode.OneTask)
                {
                    _currentTask = 0;
                    yield return StartCoroutine(Routine_ShowCurrentTask());
                }
            }

            _view.TogglePanels(true);
        }


        // ReSharper disable Unity.PerformanceAnalysis
        private IEnumerator Routine_ShowCurrentTask()
        {
            int nextTaskIndex = -1;

            // Em vez de apenas somar +1, o sistema procura dinamicamente a primeira tarefa que:
            // 1. Está livre para ser feita (IsTaskAvailable == true)
            // 2. Ainda NÃO foi feita (IsTaskDone == false)
            for (int i = 0; i < _uiTasks.Count; i++)
            {
                Task taskRef = _uiTasks[i].TaskReference;
                if (QuestSystemModel.Instance.IsTaskAvailable(taskRef) && !QuestSystemModel.Instance.IsTaskDone(taskRef))
                {
                    nextTaskIndex = i;
                    break; // Encontrou a próxima lógica, para a busca imediatamente
                }
            }

            // Se encontrou uma tarefa válida, faz o Fade In dela
            if (nextTaskIndex != -1)
            {
                _currentTask = nextTaskIndex;
                TaskUIView currentUIViewTask = _uiTasks[_currentTask];
                currentUIViewTask.gameObject.SetActive(true);

                CanvasGroup canvasGroup = currentUIViewTask.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    yield return StartCoroutine(_view.FadeRoutine(canvasGroup, 0f, 1f, _fadeDuration));
                }
            }
        }


        // quando uma tarefa é feita, será indicada como completa e será acionado o efeito sonoro
        private IEnumerator Routine_TaskUpdated(Task task)
        {
            for (int i = 0; i < _uiTasks.Count; i++)
            {
                if (_uiTasks[i].TaskReference == task)
                {
                    _uiTasks[i].TaskCompleted(); // Marca internamente/visualmente como completa

                    if (displayMode == DisplayMode.OneTask)
                    {
                        // Só faz o Fade Out e muda a UI se a task que o jogador concluiu for 
                        // exatamente aquela que estava sendo mostrada na tela.
                        // (Se ele concluiu outra task escondida, a tela não se altera)
                        if (i == _currentTask)
                        {
                            yield return new WaitForSeconds(_readDelay);

                            CanvasGroup canvasGroup = _uiTasks[i].GetComponent<CanvasGroup>();
                            if (canvasGroup != null)
                            {
                                yield return StartCoroutine(_view.FadeRoutine(canvasGroup, 1f, 0f, _fadeDuration));
                            }

                            _uiTasks[i].gameObject.SetActive(false);

                            // Pede para procurar a próxima tarefa de forma dinâmica
                            yield return StartCoroutine(Routine_ShowCurrentTask());
                        }
                    }
                    else if (displayMode == DisplayMode.AllTasks)
                    {
                        yield return null; // No modo AllTasks, o prefab da task apenas dá o check visual
                    }

                    break;
                }
            }
        }



        // ReSharper disable Unity.PerformanceAnalysis
        private IEnumerator Routine_ShowAllTasks()
        {
            foreach (TaskUIView ui in _uiTasks)
            {
                ui.gameObject.SetActive(true);

                CanvasGroup canvas = ui.GetComponent<CanvasGroup>();
                if (canvas != null) canvas.alpha = 1f;

                if (QuestSystemModel.Instance.IsTaskDone(ui.TaskReference))
                {
                    ui.TaskCompleted();
                }
            }

            yield break;
        }

        private IEnumerator Routine_QuestComplete()
        {
            _view.TogglePanels(false);
            _view.ShowQuestCompleteMessage();

            yield return new WaitForSeconds(6.0f);
            if (displayMode == DisplayMode.OneTask)
                OnQuestCompleteFinished?.Invoke();
        }
        
        public void EsconderPainelDeTasks()
        {
            if (_view != null)
            {
                _view.TogglePanels(false);
            }
        }
        

        private void OnDisable()
        {
            if (QuestSystemModel.Instance != null)
            {
                QuestSystemModel.Instance.onQuestLoaded.RemoveListener(OnQuestLoaded_Event);
                QuestSystemModel.Instance.onTaskComplete.RemoveListener(OnTaskComplete_Event);
                QuestSystemModel.Instance.onQuestComplete.RemoveListener(OnQuestComplete_Event);
            }

            StopAllCoroutines();
            _isTransitioning = false;
            _eventQueue.Clear();
        }
    }
}
