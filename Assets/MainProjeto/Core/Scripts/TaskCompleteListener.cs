using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MainProject.Trindade.Core.Scripts
{
    public class TaskCompleteListener : MonoBehaviour
    {
        [SerializeField] private Task task;
        [SerializeField] 
        private UnityEvent onComplete;
        
        [SerializeField]
        [Header("Deixar travado manualmente o interactable")]
        private bool lockInteractable = true;
        
        private XRBaseInteractable _interactable;
        
        private bool _subscribed = false;
        
        private void Awake()
        {
            if (lockInteractable)
            {
                _interactable = GetComponent<XRBaseInteractable>();
                if (lockInteractable && _interactable != null)
                {
                    //_interactable.enabled = false;
                    _interactable.interactionLayers = InteractionLayerMask.GetMask("Locked");
                }
            }
        }

        private void Start()
        {
            if (QuestSystemModel.Instance != null)
            {
                QuestSystemModel.Instance.onQuestLoaded.AddListener(OnQuestLoaded);
                QuestSystemModel.Instance.onTaskComplete.AddListener(OnAnyTaskCompleted);
                
                TrySubscribe();
                CheckIsAvailable();
            }
        }


        private void OnQuestLoaded()
        {
            TrySubscribe();
            CheckIsAvailable();
        }

        private void OnAnyTaskCompleted(Task completedTask)
        {
            CheckIsAvailable();
        }
        
        private void CheckIsAvailable()
        {
            if (_interactable == null || QuestSystemModel.Instance == null || QuestSystemModel.Instance.quest == null)
                return;

            if (QuestSystemModel.Instance.quest.tasks.Contains(task))
            {
                if (QuestSystemModel.Instance.IsTaskAvailable(task))
                {
                    //_interactable.enabled = true;
                    _interactable.interactionLayers = InteractionLayerMask.GetMask("Ray");                
                }
            }
            else
            {
                if (lockInteractable)
                {
                    //_interactable.enabled = false;
                    _interactable.interactionLayers = InteractionLayerMask.GetMask("Default");                
                }
            }
        }
        
        private void TrySubscribe()
        {
            if (!_subscribed && QuestSystemModel.Instance != null && QuestSystemModel.Instance.quest != null)
            {
                if (QuestSystemModel.Instance.quest.tasks.Contains(task))
                {
                    QuestSystemModel.Instance.SubscribeToTaskComplete(() => { onComplete.Invoke(); }, task);    
                    _subscribed = true;
                }   
            }
        }
        
        
        private void OnDestroy()
        {
            if (QuestSystemModel.Instance != null && _subscribed)
            {
                if (_subscribed)
                {
                    QuestSystemModel.Instance.UnsubscribeFromTaskComplete(() => { onComplete.Invoke(); }, task);

                }
                QuestSystemModel.Instance.onQuestLoaded.RemoveListener(OnQuestLoaded);
                QuestSystemModel.Instance.onTaskComplete.RemoveListener(OnAnyTaskCompleted);
            }
        }
    }
}