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

namespace MainProjeto.Scripts.Interactables.Tutorial
{
    public class LEDTutorial : EletronicComponent
    {
        private Material _material;

        [Header("Task para LED")] 
        [SerializeField] private Task LedTask;
    
        [Header("Feedback do Robo")]
        [SerializeField] private RobotFeedbackData feedbackCorreto;
        [SerializeField] private RobotFeedbackData feedbackIncorreto;
        
        [Header("Polaridade do LED Invertido ou não")]
        [SerializeField] private bool invertPolarity;
        
    
        protected override void Awake()
        {
            base.Awake();
            _material = GetComponentInChildren<MeshRenderer>().material;
            Debug.Log("conectado -> " + autoConnectOnAwake);
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
                    interactionLayers = InteractionLayerMask.GetMask("Locked");
                    //interactionLayers = 0;

                    if (QuestSystemModel.Instance != null)
                    {
                        QuestSystemModel.Instance.CompleteTask(LedTask);
                        
                        EnableEmission();

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
        
        
    
        private void EnableEmission() => _material.EnableKeyword("_EMISSION");
    
        //private void DisableEmission() => _material.DisableKeyword("_EMISSION");
    }
}
