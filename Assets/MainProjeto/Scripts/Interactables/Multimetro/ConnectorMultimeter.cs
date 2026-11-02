using System;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Robot.Scripts;
using MainProjeto.Scripts.ParentCoponent;
using MainProjeto.Scripts.Protoboard_Interactor;
using MainProjeto.UI.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MainProjeto.Scripts.Interactables.Multimetro
{
    public class ConnectorMultimeter : EletronicComponent
    {
        public enum ConnectorType {Red, Black}
    
        [Header("Tipo de Conector a ser definido")]
        [SerializeField] private ConnectorType connectorType;

        [Header("Task's para o Connector")]
        //[SerializeField] private Task grabTask;
        [SerializeField] private Task putTask;
    
        [Header("Feedback do robo")] 
        [SerializeField]
        private RobotFeedbackData feedbackCorreto;
        [SerializeField]
        private RobotFeedbackData feedbackIncorreto;

        [Header("Bool para definir se a medição é livre ou não")] 
        [SerializeField]
        private bool modoMedicaoLivre = false;
        public static event Action<ConnectorType, bool, Vector3Int> OnConnectorTypeChanged;
        
        
        [Header("Limites de Movimento com o cabo")]
        [SerializeField] private BoxCollider areaDeLimite;
        
        public ConnectorType GetConnectorType() => connectorType;
        

        protected override void OnEnable()
        {
            base.OnEnable();
            MeasurerMultimeter.OnMeasurementSucess += LockConector;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            MeasurerMultimeter.OnMeasurementSucess -= LockConector;
        }

        private void LockConector()
        {
            interactionLayers = InteractionLayerMask.GetMask("Locked");
        }
        
        public override bool ValidatePins(ProtoboardInteractor protoboard)
        {
            // Ignora as regras rígidas e permite que a ponta conecte em qualquer buraco livre
            List<Vector3Int> positions = protoboard.GetTargetCells(this);
            return positions != null && positions.Count > 0;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase phase)
        {
            base.ProcessInteractable(phase);

            // Executa a restrição apenas durante o cálculo dinâmico de movimento e enquanto o objeto é segurado
            if (phase == XRInteractionUpdateOrder.UpdatePhase.Dynamic && isSelected && areaDeLimite != null)
            {
                // ClosestPoint retorna a posição exata se estiver dentro da caixa.
                // Se o jogador tentar puxar para fora, ele retorna o ponto máximo na borda da caixa.
                transform.position = areaDeLimite.ClosestPoint(transform.position);
            }
        }
        
        
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
        
            if (args.interactorObject is not ProtoboardInteractor)
            {
                isAutoConnected = false;
                if (IsOriginCircuitActive)
                {
                    OnConnectorTypeChanged?.Invoke(connectorType, false, Vector3Int.zero);
                }
            
                /*if (QuestSystemModel.Instance != null && grabTask != null)
                {
                    QuestSystemModel.Instance.CompleteTask(grabTask);
                }*/
            
            }

            if (args.interactorObject is ProtoboardInteractor protoboard)
            {
                bool correctPosition = ValidatePins(protoboard);

                if (correctPosition)
                {

                    List<Vector3Int> positions = protoboard.GetTargetCells(this);
                    if (positions.Count > 0)
                    {
                        if (IsOriginCircuitActive)
                        {
                            OnConnectorTypeChanged?.Invoke(connectorType, true, positions[0]);
                        }

                        if (!modoMedicaoLivre)
                        {
                            LockConector();
                            if (QuestSystemModel.Instance != null && putTask != null)
                            {
                                QuestSystemModel.Instance.CompleteTask(putTask);

                                if (!isAutoConnected && RobotFeedbackManager.Instance != null)
                                {
                                    RobotFeedbackManager.Instance.TriggerFeedback(feedbackCorreto);
                                }

                                if (!isAutoConnected && SessionMetricsManager.Instance != null)
                                {
                                    SessionMetricsManager.Instance.RegisterSucess();
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (!isAutoConnected && !modoMedicaoLivre)
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
