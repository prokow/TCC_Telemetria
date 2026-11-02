using System.Collections;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using UnityEngine;
using UnityEngine.Events;

namespace MainProjeto.Robot.Scripts
{
    public class SequentialRobotNarrative : MonoBehaviour
    {
        [Header("Sequência de Falas do Robô")]
        [SerializeField] private List<RobotFeedbackData> falas;
        [SerializeField] private float tempoEntreFalas = 20f;
        [Tooltip("Tarefa concluida pela narrativa ou entregue ao botao como ultima tarefa pendente.")]
        [SerializeField] private Task falaRoboTask;
        [Header("Concluir a tarefa automaticamente ao terminar a narrativa")]
        [SerializeField] private bool autoConcluir = false;
        [Header("Botao fisico usado quando Auto Concluir esta desmarcado")]
        [SerializeField] private NextQuestButton botaoProximaQuest;
        [Header("Opcional: evento ao iniciar a narrativa")]
        [SerializeField] private UnityEvent onInitiateNarrative;

        private bool _isRoboFala;
        private QuestSystemModel model;
        private Quest narrativeQuest;

        private void OnEnable() => BindModel();
        private void Start() => BindModel();
        private void BindModel()
        {
            var current = QuestSystemModel.Instance;
            if (current != model)
            {
                UnbindModel(); model = current;
                if (model != null)
                {
                    model.onTaskComplete.AddListener(VerificarDependencias);
                    model.onQuestLoaded.AddListener(OnQuestLoaded);
                }
            }
            TryStartNarrative();
        }
        private void UnbindModel()
        {
            if (model != null)
            {
                model.onTaskComplete.RemoveListener(VerificarDependencias);
                model.onQuestLoaded.RemoveListener(OnQuestLoaded);
            }
            model = null;
        }
        private void OnDisable() { StopAllCoroutines(); UnbindModel(); _isRoboFala = false; narrativeQuest = null; }
        private void OnDestroy() => UnbindModel();
        private void OnQuestLoaded() { StopAllCoroutines(); _isRoboFala = false; narrativeQuest = null; TryStartNarrative(); }
        private void VerificarDependencias(Task _) => TryStartNarrative();
        private void TryStartNarrative()
        {
            if (!isActiveAndEnabled || _isRoboFala || model == null || falaRoboTask == null) return;
            if (!QuestAdvanceRules.CanStartNarrative(model.IsTaskAvailable(falaRoboTask), autoConcluir, model.CanPrepareFinalNarrative(falaRoboTask))) return;
            _isRoboFala = true; narrativeQuest = model.quest;
            onInitiateNarrative?.Invoke();
            if (IsCurrentNarrative()) StartCoroutine(PlayNarrativeRoutine());
        }
        private bool IsCurrentNarrative() => isActiveAndEnabled && model != null && model.quest == narrativeQuest;
        private IEnumerator PlayNarrativeRoutine()
        {
            yield return new WaitUntil(() => !IsCurrentNarrative() || !model.IsTransitionInProgress);
            if (!IsCurrentNarrative()) yield break;
            yield return new WaitUntil(() => RobotFeedbackManager.Instance != null || !IsCurrentNarrative());
            if (!IsCurrentNarrative()) yield break;
            yield return new WaitForSeconds(1f);
            if (falas == null || falas.Count == 0)
            { Debug.LogWarning("Narrativa sem falas: confira o prefab.", this); yield break; }
            foreach (var fala in falas)
            {
                if (!IsCurrentNarrative()) yield break;
                if (fala == null || fala.sound == null)
                { Debug.LogWarning("Narrativa com fala/audio ausente: confira o prefab antes de liberar a fase.", this); yield break; }
                RobotFeedbackManager.Instance.TriggerFeedback(fala);
                float waitTime = fala.sound.length / Mathf.Max(0.1f, Mathf.Abs(fala.pitch));
                yield return new WaitForSeconds(waitTime + Mathf.Max(0f, tempoEntreFalas));
            }
            if (!IsCurrentNarrative()) yield break;
            if (autoConcluir) model.CompleteTask(falaRoboTask);
            else if (model.CanAdvanceAfterTask(falaRoboTask) && botaoProximaQuest != null)
                botaoProximaQuest.TurnOnButton(falaRoboTask);
        }
    }
}
