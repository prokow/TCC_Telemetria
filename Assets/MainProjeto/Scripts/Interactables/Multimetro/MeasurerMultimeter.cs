using System;
using System.Collections.Generic;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using MainProjeto.Core.Scripts.Model;
using MainProjeto.Scripts.Interactables.LEDcomBotao;
using TMPro;
using UnityEngine;

namespace MainProjeto.Scripts.Interactables.Multimetro
{
    [System.Serializable]
    public struct ConnectoArea
    {
        public Vector3Int minCell;
        public Vector3Int maxCell;

        public bool Contains(Vector3Int cell)
        {
            return cell.x >= minCell.x && cell.x <= maxCell.x &&
                   cell.y >= minCell.y && cell.y <= maxCell.y;
        }
    }

    [System.Serializable]
    public struct DualConnectorReading
    {
        public ConnectoArea redTargetCell;
        public ConnectoArea blackTargetCell;
        public string displayValue;

        public bool isCorrectMeasurement;
    }

    public class MeasurerMultimeter : MonoBehaviour
    {
        [Header("Posições a se conectar ambos os conectores")]
        [SerializeField]
        private List<DualConnectorReading> m_positions = new List<DualConnectorReading>();

        [Header("Quantidade de resistores no circuito")]
        [SerializeField] 
        private int requiredResistors = 1;

        [Header("Task de Medição Correta")] 
        [SerializeField]
        private Task measureTask;

        private readonly HashSet<ResistorController> _connectedResistors = new HashSet<ResistorController>();
    
        private TextMeshProUGUI m_medidor;
    
        private bool isRedConnected = false;
        private Vector3Int currentRedCell;
    
        private bool isBlackConnected = false;
        private Vector3Int currentBlackCell;

        public static event Action OnMeasurementSucess;
        
        public static List<MeasurerMultimeter> ActiveMeasurers = new List<MeasurerMultimeter>();

        
        //private bool isResistorConnected = false;
    
        void Awake()
        {
            m_medidor = GetComponentInChildren<TextMeshProUGUI>();
            if (m_medidor == null)
            {
                Debug.Log("nao achou textmeshpro");
            }
        }
        
        private void Start()
        {
            if (QuestSystemModel.Instance != null && measureTask != null)
            {
                // 1. Verifica logo no início caso a task já comece libertada (ex: sem dependências)
                if (QuestSystemModel.Instance.IsTaskAvailable(measureTask))
                {
                    CalculateMeasurement();
                }

                // 2. Fica a ouvir sempre que QUALQUER tarefa terminar no jogo
                QuestSystemModel.Instance.onTaskComplete.AddListener(OnAnyTaskCompleted);
            }
            else if (measureTask == null)
            {
                Debug.LogWarning("A Measure Task não foi atribuída no Inspector do MeasurerMultimeter!");
            }
        }

        private void OnDestroy()
        {
            if (QuestSystemModel.Instance != null && measureTask != null)
            {
                QuestSystemModel.Instance.onTaskComplete.RemoveListener(OnAnyTaskCompleted);
            }
        }
        
        private void OnAnyTaskCompleted(Task completedTask)
        {
            // Se a tarefa que acabou de terminar libertou o caminho para a medição, o multímetro atua
            if (QuestSystemModel.Instance.IsTaskAvailable(measureTask))
            {
                CalculateMeasurement();
            }
        }
        
        private void OnEnable()
        {
            // Adiciona este multímetro à lista quando a Quest o ligar
            ActiveMeasurers.Add(this);
    
            ConnectorMultimeter.OnConnectorTypeChanged += UpdateConnectorState;
            ResistorController.OnResistorStateChanged += UpdateResistorState;
        }

        private void OnDisable()
        {
            // Remove este multímetro da lista quando a Quest o desligar
            ActiveMeasurers.Remove(this);
            _connectedResistors.Clear();
    
            ConnectorMultimeter.OnConnectorTypeChanged -= UpdateConnectorState;
            ResistorController.OnResistorStateChanged -= UpdateResistorState;
        }

        public bool IsValidCellForConnector(ConnectorMultimeter.ConnectorType type, Vector3Int cell)
        {
            foreach (var reading in m_positions)
            {
                if (type == ConnectorMultimeter.ConnectorType.Red && reading.redTargetCell.Contains(cell))
                    return true;
        
                if (type == ConnectorMultimeter.ConnectorType.Black && reading.blackTargetCell.Contains(cell))
                    return true;
            }
            return false;
        }

        public static bool CheckIfAnyMeasurerAccepts(ConnectorMultimeter.ConnectorType type, Vector3Int cell)
        {
            // Se não houver nenhum multímetro ativo, permite tudo (ou retorne false se quiser bloquear)
            if (ActiveMeasurers.Count == 0) return true; 

            foreach (var measurer in ActiveMeasurers)
            {
                if (measurer.IsValidCellForConnector(type, cell))
                    return true;
            }
            return false;
        }

        private void UpdateConnectorState(ConnectorMultimeter.ConnectorType color, bool isConnected, Vector3Int cell)
        {
            if (color == ConnectorMultimeter.ConnectorType.Red)
            {
                isRedConnected = isConnected;
                currentRedCell = cell;
            } else if (color == ConnectorMultimeter.ConnectorType.Black)
            {
                isBlackConnected = isConnected;
                currentBlackCell = cell;
            }

            CalculateMeasurement();
        }

        private void UpdateResistorState(ResistorController resistor, bool isConnected)
        {
            if (resistor == null) return;

            if (isConnected)
            {
                if (!resistor.IsOriginCircuitActive) return;
                _connectedResistors.Add(resistor);
            }
            else
            {
                _connectedResistors.Remove(resistor);
            }

            CalculateMeasurement();
        }

        private void CalculateMeasurement()
        {
            _connectedResistors.RemoveWhere(r => r == null || !r.IsOriginCircuitActive);

            if (!isRedConnected || !isBlackConnected || _connectedResistors.Count < requiredResistors)
            {
                if(m_medidor != null) m_medidor.text = "0.00";
                return;
            }

            string newValue = "0.00";
            bool isValidAndCorrect = false;

            foreach (var reading in m_positions)
            {
                if (reading.redTargetCell.Contains(currentRedCell) && 
                    reading.blackTargetCell.Contains(currentBlackCell))
                {
                    newValue = reading.displayValue;
                    isValidAndCorrect = reading.isCorrectMeasurement;
                    break;
                }
            }

            if (m_medidor != null)
            {
                m_medidor.text = newValue;
                Debug.Log("valor: " + newValue);

                if (isValidAndCorrect)
                {
                    OnMeasurementSucess?.Invoke();
                    
                    if (QuestSystemModel.Instance != null && measureTask != null)
                    {
                        QuestSystemModel.Instance.CompleteTask(measureTask);
                        //Debug.Log("task terminada");
                    } 
                }
            }
        }
    }
}
