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
    public class ButtonController : EletronicComponent
    {
    
        [Header("Task para colocar o botão no lugar certo na Protoboard")]
        //[SerializeField] private Task grabButtonTask;
        [SerializeField] private Task putButtonTask;


        [Header("Feedback do Robo")] 
        [SerializeField] private RobotFeedbackData feedbackCorreto;
        [SerializeField] private RobotFeedbackData feedbackIncorreto;
        
        private PushButtonSwitch _pushButton;

        protected override void Awake()
        {
            base.Awake();
            if (_pushButton == null)
            {
                _pushButton = GetComponentInChildren<PushButtonSwitch>();
            }
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);

            /*if (args.interactorObject is not ProtoboardInteractor)
            {
                if (QuestSystemModel.Instance != null)
                {
                    QuestSystemModel.Instance.CompleteTask(grabButtonTask);
                }   
            }*/
        
        
            if (args.interactorObject is ProtoboardInteractor protoboard)
            {
                bool correctPosition = ValidatePins(protoboard);

                if (correctPosition)
                {
                    //interactionLayers = LayerMask.GetMask("Default");
                    interactionLayers = InteractionLayerMask.GetMask("Locked");
                    
                    if (QuestSystemModel.Instance != null)
                    {
                        QuestSystemModel.Instance.CompleteTask(putButtonTask);
                        
                        if (!isAutoConnected && RobotFeedbackManager.Instance != null && feedbackCorreto != null)
                        {
                            RobotFeedbackManager.Instance.TriggerFeedback(feedbackCorreto);
                        }
                        
                        if (!isAutoConnected && SessionMetricsManager.Instance != null)
                        {
                            SessionMetricsManager.Instance.RegisterSucess();
                        }
                    }
                } else 
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
    }
}
