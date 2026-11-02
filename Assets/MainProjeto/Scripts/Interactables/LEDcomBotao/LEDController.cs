using System.Collections;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Robot.Scripts;
using MainProjeto.Scripts.ParentCoponent;
using MainProjeto.Scripts.Protoboard_Interactor;
using MainProjeto.UI;
using MainProjeto.UI.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MainProjeto.Scripts.Interactables.LEDcomBotao
{
    public class LedController : EletronicComponent
    {
        private Material _material;
        private bool _isConnectLed = false; 
    
        [Header("GameObject do botão para acender o LED")]
        [SerializeField]
        private PushButtonSwitch pushButtonSwitch;

        [Header("Task para colocar LED no lugar correto")] 
        [SerializeField] 
        private Task LedTask;
        [Header("Task para quando o LED acender")]
        [SerializeField]
        private Task lightUpTask;
    
        [Header("Feedback do Robo")]
        [SerializeField] private RobotFeedbackData feedbackCorreto;
        [SerializeField] private RobotFeedbackData feedbackIncorreto;
        
        [Header("Polaridade do LED Invertido ou não")]
        [SerializeField] private bool invertPolarity;
        
    
        protected override void Awake()
        {
            base.Awake();
            _material = GetComponentInChildren<MeshRenderer>().material;
        }
        
        

        private bool ValidatePolarity(ProtoboardInteractor protoboard, List<Vector3Int> footprint)
        {
            if (footprint == null || footprint.Count < 2) return false;
            
            Vector3Int cellAnodo = footprint[0];
            Vector3Int cellCatodo = footprint[1];

            if (invertPolarity)
            {
                return cellAnodo.x < cellCatodo.x;
            }
            else
            {
                return cellAnodo.x > cellCatodo.x;   
            }
        }

        public override bool ValidatePins(ProtoboardInteractor protoboard)
        {
            if (!base.ValidatePins(protoboard))
            {
                return false;
            }

            List<Vector3Int> footprint = protoboard.GetTargetCells(this);

            bool isPolarityCorrect = ValidatePolarity(protoboard, footprint);

            return isPolarityCorrect;
        }


        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            if(_isConnectLed) return;
            base.OnSelectEntered(args);
            
            /*if (args.interactorObject is not ProtoboardInteractor)
            {
                if (QuestSystemModel.Instance != null)
                {
                    QuestSystemModel.Instance.CompleteTask(grabLedTask);
                }
            }*/
            
            if (args.interactorObject is ProtoboardInteractor protoboard)
            {
                bool correctPosition = ValidatePins(protoboard);

                if (correctPosition)
                {
                    _isConnectLed = true;
                    interactionLayers = InteractionLayerMask.GetMask("Locked");
                    
                    if (QuestSystemModel.Instance != null && LedTask != null)
                    {
                        QuestSystemModel.Instance.CompleteTask(LedTask);

                        if (!isAutoConnected && RobotFeedbackManager.Instance != null && feedbackCorreto != null)
                        {
                            RobotFeedbackManager.Instance.TriggerFeedback(feedbackCorreto);
                            Debug.Log("feedback - " + feedbackCorreto);
                        }
                        
                        if (!isAutoConnected && SessionMetricsManager.Instance != null)
                        {
                            SessionMetricsManager.Instance.RegisterSucess();
                        }
                    }
                }
                else
                {
                    if (!isAutoConnected)
                    {
                        if (SessionMetricsManager.Instance != null)
                        {
                            SessionMetricsManager.Instance.RegisterError();
                        }
                    
                        if (RobotFeedbackManager.Instance != null && feedbackIncorreto != null)
                        {
                            RobotFeedbackManager.Instance.TriggerFeedback(feedbackIncorreto);
                        }
                    }
                }
            }
        }

  

        protected override void OnEnable()
        {
            base.OnEnable();
            if (pushButtonSwitch != null)
            { 
                pushButtonSwitch.OnButtonPressed += EnableEmission;
                pushButtonSwitch.OnButtonReleased += DisableEmission;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (pushButtonSwitch != null)
            {
                pushButtonSwitch.OnButtonPressed -= EnableEmission;
                pushButtonSwitch.OnButtonReleased -= DisableEmission;   
            }
        }
        
        private void EnableEmission()
        {
            if (!_isConnectLed || _material == null)
                return;

            // Usa a protoboard à qual este LED está conectado.
            ProtoboardInteractor connectedBoard = null;

            foreach (var interactor in interactorsSelecting)
            {
                if (interactor is ProtoboardInteractor board)
                {
                    connectedBoard = board;
                    break;
                }
            }

            if (connectedBoard == null || !ValidatePins(connectedBoard))
                return;

            _material.EnableKeyword("_EMISSION");

            // A tarefa de acender pode depender da tarefa de pressionar.
            // Confirma primeiro o pressionamento, agora que o LED conseguiu acender.
            pushButtonSwitch?.ConfirmLedLit();

            if (QuestSystemModel.Instance != null && lightUpTask != null)
            {
                QuestSystemModel.Instance.CompleteTask(lightUpTask);
            }
        }

        private void DisableEmission()
        {
            _material.DisableKeyword("_EMISSION");
        }
        
    }
}
