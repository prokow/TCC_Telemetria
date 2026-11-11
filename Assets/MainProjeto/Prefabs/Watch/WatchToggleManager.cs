using System;
using System.Collections;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.Quest_UI;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MainProjeto.Prefabs.Watch
{
    public class WatchToggleManager : MonoBehaviour
    {
        [Header("Watch Settings")]
        [SerializeField]
        private Canvas _canvasWatch;

        [Header("Watch Button Settings")]
        [SerializeField] 
        private InputActionReference toggleButton;
        
        private bool _isVisible;
        private bool _isUnlocked;

        [Header("Quest's onde vai ficar disponiveis as atividades no braço")]
        [SerializeField] private List<Quest> _requisitionQuest = new List<Quest>();

        private void Awake()
        {
            if (_canvasWatch == null)
            {
                Debug.Log("canvasWatch or gameObjectWatch not found");
            }

            if (toggleButton == null)
            {
                Debug.Log("not declared a input action reference");
            }
            
            _isUnlocked = false;
            if (_canvasWatch != null)
            {
                _canvasWatch.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (toggleButton != null && toggleButton.action != null)
            {
                toggleButton.action.Enable();
                toggleButton.action.performed += ToggleWatch;
            }

            StartCoroutine(WaitingQuestSystem());
        }

        private void OnDisable()
        {
            if (toggleButton != null && toggleButton.action != null)
            {
                toggleButton.action.performed -= ToggleWatch;   
            }

            if (QuestSystemModel.Instance != null)
            {
                QuestSystemModel.Instance.onQuestLoaded.RemoveListener(VerificarPermissaoQuest);
            }

            StopAllCoroutines();
        }

        private IEnumerator WaitingQuestSystem()
        {
            while (QuestSystemModel.Instance == null)
            {
                yield return null;
            }

            QuestSystemModel.Instance.onQuestLoaded.RemoveListener(VerificarPermissaoQuest);
            QuestSystemModel.Instance.onQuestLoaded.AddListener(VerificarPermissaoQuest);

            if (QuestSystemModel.Instance.quest != null)
            {
                VerificarPermissaoQuest();
            }
        }
        
        private void VerificarPermissaoQuest()
        {
            if (QuestSystemModel.Instance == null) return;

            Quest questAtual = QuestSystemModel.Instance.quest;
            bool questPermitida = questAtual != null && _requisitionQuest != null && _requisitionQuest.Contains(questAtual);

            if (questPermitida)
            {
                if (!_isUnlocked)
                {
                    _isUnlocked = true;

                    if (_canvasWatch != null)
                    {
                        _canvasWatch.gameObject.SetActive(false);
                    }
                    //Debug.Log("Relógio desbloqueado para uso!");
                }
            }
            else
            {
                _isUnlocked = false;

                if (_canvasWatch != null)
                {
                    _canvasWatch.gameObject.SetActive(false);
                }
            }
        }

        private void ToggleWatch(InputAction.CallbackContext context)
        {
            if (!_isUnlocked) return;
            
            if (_canvasWatch != null)
            {
                bool isVisualActive = _canvasWatch.gameObject.activeSelf;
                _canvasWatch.gameObject.SetActive(!isVisualActive);
            }
        }
    }
}
