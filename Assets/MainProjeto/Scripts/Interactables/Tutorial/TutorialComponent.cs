using System.Collections.Generic;
using MainProjeto.Robot.Scripts;
using MainProjeto.Scripts.ParentCoponent;
using MainProjeto.Scripts.Protoboard_Interactor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MainProjeto.Scripts.Tutorial
{
    public class TutorialComponent : EletronicComponent
    {

        [Header("Feedback do Robo")]
        [SerializeField] 
        private RobotFeedbackData feedbackCorreto;
        [SerializeField]
        private RobotFeedbackData feedbackIncorreto;
    
        // ReSharper disable Unity.PerformanceAnalysis
        public override bool ValidatePins(ProtoboardInteractor protoboard)
        {
            if (!base.ValidatePins(protoboard)) return false;

            List<Vector3Int> footprint = protoboard.GetTargetCells(this);


            foreach (Vector3Int cell in footprint)
            {
                bool validY = cell.y <= -1 && cell.y >= -10;
                bool validX = cell.x <= -1 && cell.x >= -12;

                if (!validY || !validX)
                {
                    return false;
                }
            }

            return true;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);

            if (args.interactorObject is not ProtoboardInteractor)
            {
                //if (_img != null) _img.color = Color.yellow;
                //QuestSystem.Instance.CompleteTask(grabTask);
            }


            if (args.interactorObject is ProtoboardInteractor protoboard)
            {
                bool correctPosition = ValidatePins(protoboard);
            
                if(correctPosition)
                {
                    interactionLayers = LayerMask.GetMask("Default");

                    if (RobotFeedbackManager.Instance != null && feedbackCorreto != null)
                    {
                        RobotFeedbackManager.Instance.TriggerFeedback(feedbackCorreto);
                    }
                }
                else
                {
                    if (RobotFeedbackManager.Instance != null && feedbackIncorreto != null)
                    {
                        RobotFeedbackManager.Instance.TriggerFeedback(feedbackIncorreto);
                    }
                }
            }
        }
    }
}
