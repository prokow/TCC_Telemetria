using System;
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
    public class ResistorController : EletronicComponent
    {
    
        [Header("Task para colocar na protoboard o resistor")]
        [SerializeField] private Task putResistorTask;

        [Header("Feedback do robo")] 
        [SerializeField] private RobotFeedbackData feedbackCorreto;
        [SerializeField] private RobotFeedbackData feedbackIncorreto;
        
        public static event Action<ResistorController, bool> OnResistorStateChanged;

        private bool isConnectedCorrectly = false;

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
       
            if (args.interactorObject is ProtoboardInteractor protoboard)
            {
                bool correctPosition = ValidatePins(protoboard);

                if (correctPosition)
                {
                    isConnectedCorrectly = true;
                    interactionLayers = InteractionLayerMask.GetMask("Locked");
                    //interactionLayers = 0;
                    
                    if (IsOriginCircuitActive)
                    {
                        OnResistorStateChanged?.Invoke(this, true);
                    }
                    

                    if (QuestSystemModel.Instance != null && putResistorTask != null)
                    {
                        QuestSystemModel.Instance.CompleteTask(putResistorTask);
                    
                        if (!isAutoConnected && RobotFeedbackManager.Instance != null && feedbackCorreto != null)
                        {
                            RobotFeedbackManager.Instance.TriggerFeedback(feedbackCorreto);
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

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (isConnectedCorrectly)
            {
                isConnectedCorrectly = false;
                if (IsOriginCircuitActive)
                {
                    OnResistorStateChanged?.Invoke(this, false);
                }
            }
        }
    }
}

