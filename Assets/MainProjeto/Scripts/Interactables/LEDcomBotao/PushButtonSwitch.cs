using System;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Core.Scripts.ScriptableObjects;
using QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MainProjeto.Scripts.Interactables.LEDcomBotao
{
    [RequireComponent(typeof(Outline))]
    public class PushButtonSwitch : XRSimpleInteractable
    {
        private Outline _outline;
    
        [Header("Task para apertar o botão para ligar o LED")]
        [SerializeField]
        private Task pressTask;

        [Tooltip("Quando marcado, a tarefa de pressionar so termina apos o LED confirmar o acendimento.")]
        [SerializeField] 
        private bool requireLedConfirmation = false;
        
        public event Action OnButtonPressed;
        public event Action OnButtonReleased;
    
        protected override void Awake()
        {
            base.Awake();
        
            _outline = GetComponent<Outline>();
        
            _outline.OutlineMode = Outline.Mode.OutlineAll;
            _outline.OutlineWidth = 8.0f;
            _outline.OutlineColor = Color.yellow;
            _outline.enabled = false;
        }

        
        public override bool IsHoverableBy(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRHoverInteractor interactor)
        {
            if (!base.IsHoverableBy(interactor)) return false;

            if (pressTask != null && QuestSystemModel.Instance != null)
            {
                bool isAvailable = QuestSystemModel.Instance.IsTaskAvailable(pressTask);
                bool isDone = QuestSystemModel.Instance.IsTaskDone(pressTask);
        
                // Libera o botão se a tarefa estiver pronta para ser feita, OU se já foi feita.
                return isAvailable || isDone; 
            }
            return true; // Se não tiver task atribuída, fica livre por padrão
        }

        public override bool IsSelectableBy(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor)) return false;

            if (pressTask != null && QuestSystemModel.Instance != null)
            {
                bool isAvailable = QuestSystemModel.Instance.IsTaskAvailable(pressTask);
                bool isDone = QuestSystemModel.Instance.IsTaskDone(pressTask);
        
                return isAvailable || isDone;
            }
            return true;
        }
        

        protected override void OnHoverEntered(HoverEnterEventArgs args)
        {
            base.OnHoverEntered(args);
            if(_outline != null) _outline.enabled = true;
            
        }

        protected override void OnHoverExited(HoverExitEventArgs args)
        {
            base.OnHoverExited(args);
            if(_outline != null) _outline.enabled = false;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            
            if (!requireLedConfirmation && QuestSystemModel.Instance != null && pressTask != null)
            {
                QuestSystemModel.Instance.CompleteTask(pressTask);
            }
            OnButtonPressed?.Invoke();
        }


        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            OnButtonReleased?.Invoke();
        }


        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic)
            {
                transform.localPosition = isSelected ? new Vector3(0f, 0f, -1.19e-05f) : new Vector3(0f, 0f, 0f);
            }
        }

        // Chamado pelo LED somente depois de validar os pinos e ativar a emissao.
        public void ConfirmLedLit()
        {
            if (requireLedConfirmation && pressTask != null && QuestSystemModel.Instance != null)
            {
                QuestSystemModel.Instance.CompleteTask(pressTask);
            }
        }

        // Preserva eventuais chamadas ja configuradas no Inspector com o nome anterior.
        public void ConfirmLed() => ConfirmLedLit();
    }
}
