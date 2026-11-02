using System;
using System.Collections.Generic;
using MainProjeto.Scripts.Protoboard_Interactor;
using QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Object = UnityEngine.Object;

namespace MainProjeto.Scripts.ParentCoponent
{
    
    //[RequireComponent(typeof(Outline))]
    public class EletronicComponent : XRGrabInteractable
    {
        
        public enum RotationAxis
        {
            AxisX,
            AxisY,
            AxisZ
        }

        [Header("Este Componente serve para qual tipo de fileira?")]
        [SerializeField] 
        private bool canConnectToGround = true;
        [SerializeField] 
        private bool canConnectToVcc = true;
        
        //[Header("Definir se o Componente pode rotacionar 90 graus ou não")]
        [HideInInspector] [SerializeField] 
        private bool canRotateComponent = true;

        [Header("Controles")]
        [HideInInspector] [SerializeField] private GameObject prefabRotacao;
        [HideInInspector] [SerializeField] private float alturaDica = 0.15f;
        private GameObject _dicaInstanciada;
        private Camera _mainCamera;
        private XRGrabInteractable _grabInteractable;
        
        [HideInInspector] [SerializeField]
        private RotationAxis _rotationToUse = RotationAxis.AxisZ;

        [Header("Quantas celulas será ocupadas para determinado lado")]
        public int leftExtend = 0;
        public int rightExtend = 0;
        public int topExtend = 0;
        public int bottomExtend = 0;

        [Header("Transform do attach point para conectar na Protoboard")]
        [SerializeField] 
        private Transform _attachPoint;
        
        [Header("Transform usado para rotacionar o mesh e os colliders")]
        [SerializeField]
        private Transform pivotInterno;
        
        
        private Outline _outline;
        private Rigidbody _rb;
        private bool _isRotated = false;

        [HideInInspector] [SerializeField]
        private BoxCollider movementArea;
        [HideInInspector] [SerializeField]
        private OutsideComponentAreaAction outsideAreaAction = OutsideComponentAreaAction.ReturnToLastValidPosition;
        private ElectronicComponentMovementLimit _movementLimit;
        
        
        
        //[Header("Conexão Automática (Início da Cena)")]
        [HideInInspector] [SerializeField] 
        protected bool autoConnectOnAwake = false;
        
        [HideInInspector]
        [SerializeField] 
        protected Vector3Int autoConnectPin = new Vector3Int(-2, -4, 0);
        
        [HideInInspector] 
        public bool isAutoConnected = false; 

        //[Header("Restrição de Área Específica (Opcional)")]
        [Tooltip("Marque se este componente só puder ser conectado em colunas e limites específicos")]
        [HideInInspector] [SerializeField] 
        protected bool hasSpecificLimits = false;
        [HideInInspector] [SerializeField] 
        protected int _limitMinX = -3;
        [HideInInspector] [SerializeField] 
        protected int _limitMaxX = -3;
        [HideInInspector] [SerializeField] 
        protected int _limitMinY = -5;
        [HideInInspector] [SerializeField] 
        protected int _limitMaxY = -3;


        private Transform _originalParentFolder;
        private bool _hadParent = false;

        public bool IsOriginCircuitActive => !_hadParent || (_originalParentFolder != null && _originalParentFolder.gameObject.activeInHierarchy);


        private Vector3 _spawnPosition;
        
        protected override void Awake()
        {
            base.Awake();

            if (transform.parent != null)
            {
                _originalParentFolder = transform.parent;
                _hadParent = true;
            }
            
            _outline = GetComponentInParent<Outline>();
            if (_outline != null)
            {
                _outline.OutlineMode = Outline.Mode.OutlineAll;
                _outline.OutlineWidth = 8.0f;
                _outline.OutlineColor = Color.yellow;
                _outline.enabled = false;
            }
            _rb = GetComponent<Rigidbody>();
            _spawnPosition = transform.position;
            _movementLimit = new ElectronicComponentMovementLimit(this, _rb, pivotInterno);

            if (autoConnectOnAwake)
            {
                isAutoConnected = true;
            }

            farAttachMode = InteractableFarAttachMode.Near;
            
            _grabInteractable = GetComponent<XRGrabInteractable>();

        }

        protected virtual void Start()
        {
            if (autoConnectOnAwake)
            {
                InstantConnect(autoConnectPin);
            }

            
            _mainCamera = Camera.main;
    
            if (prefabRotacao != null)
            {
                // Instancia a dica no mundo, mas começa desligada
                _dicaInstanciada = Instantiate(prefabRotacao);
                _dicaInstanciada.SetActive(false);
            }
            
        }
        
        
        private void LateUpdate()
        {
            // Start/InstantConnect ja ocorreram. Pecas selecionadas pela protoboard sao ignoradas.
            _movementLimit?.Tick(movementArea, outsideAreaAction);
            if (_dicaInstanciada != null && _dicaInstanciada.activeSelf && _mainCamera != null)
            {
                _dicaInstanciada.transform.position = transform.position + Vector3.up * alturaDica;
                Vector3 toCamera = _mainCamera.transform.position - _dicaInstanciada.transform.position;
                _dicaInstanciada.transform.rotation = Quaternion.LookRotation(-toCamera);
            }
        }

        protected override void OnDestroy()
        {
            // Limpa a memória se a peça eletrônica for deletada
            if (_dicaInstanciada != null) 
                Destroy(_dicaInstanciada);
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

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            _movementLimit?.BeforeSelect(args.interactorObject);
            base.OnSelectEntering(args);
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            return (_movementLimit == null || _movementLimit.AllowsSelection(interactor))
                && base.IsSelectableBy(interactor);
        }

        protected override void Detach()
        {
            if (_movementLimit == null || !_movementLimit.PendingDetach) { base.Detach(); return; }
            // O XR aplica o arremesso no fim do frame. Nao reaplicar essa velocidade apos recuperar.
            bool previousThrow = throwOnDetach;
            throwOnDetach = false;
            try { base.Detach(); }
            finally { throwOnDetach = previousThrow; _movementLimit.FinishDetach(); }
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if(_rb != null) _rb.isKinematic = true;
            if (_outline != null) _outline.enabled = false;
    
            // Liga a dica de rotação
            if (canRotateComponent && _dicaInstanciada != null && args.interactorObject is not ProtoboardInteractor)
            {
                _dicaInstanciada.SetActive(true);
            }
                
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (_rb != null) _rb.isKinematic = false;
    
            if (args.interactorObject is not ProtoboardInteractor)
            {
                isAutoConnected = false; 
            }
    
            // Desliga a dica de rotação
            if (_dicaInstanciada != null)
            {
                _dicaInstanciada.SetActive(false);   
            }
        }

        
        
        private void Update()
        {
            if (!_hadParent) return;

            // Se a pasta original foi apagada (null) OU se foi desligada pelo QuestSystem
            if (_originalParentFolder == null || !_originalParentFolder.gameObject.activeInHierarchy)
            {
                // A peça destrói-se automaticamente para não aparecer na próxima quest
                Destroy(gameObject);
            }
        }

        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            /*if(!base.IsHoverableBy(interactor)) return false;

            if (interactor is ProtoboardInteractor protoboard)
            {
                return ValidatePins(protoboard);
            }

            return true;*/
            return base.IsHoverableBy(interactor);
        }

        /*public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if(!base.IsSelectableBy(interactor)) return false;

            if (interactor is ProtoboardInteractor protoboard)
            {
                return ValidatePins(protoboard);
            }
            return true;
        }*/

        protected override void OnActivated(ActivateEventArgs args)
        {
            base.OnActivated(args);

            if (pivotInterno != null && canRotateComponent)
            {
                switch (_rotationToUse)
                {
                    case RotationAxis.AxisX:
                        pivotInterno.Rotate(90f, 0, 0, Space.Self);
                        break;
                    
                    case RotationAxis.AxisY:
                        pivotInterno.Rotate(0, 90f, 0, Space.Self);
                        break;
                    
                    case RotationAxis.AxisZ:
                        pivotInterno.Rotate(0, 0, 90f, Space.Self);
                        break;
                }

                _isRotated = !_isRotated;
                
                Debug.Log($"O rotate deste componente esta ativado para o eixo {_rotationToUse}");
            }
            else if(!canRotateComponent)
            {
                Debug.Log("O rotate deste componente esta desativado");
            }
        }
        
        
        public void RespawnToOrigin()
        {
            transform.position = _spawnPosition;
    
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }
        


        public virtual bool ValidatePins(ProtoboardInteractor protoboard)
        {
            List<Vector3Int> footprint = protoboard.GetTargetCells(this);
            Grid grid = protoboard.GetComponent<Grid>();

            foreach (Vector3Int cell in footprint)
            {
                Vector3 worldPos = grid.GetCellCenterWorld(cell);
                ProtoboardInteractor.CircuitArea area = protoboard.GetCircuitArea(worldPos);

                switch (area)
                {
                    case ProtoboardInteractor.CircuitArea.None:
                        return false;
                    case ProtoboardInteractor.CircuitArea.Ground:
                        if (!canConnectToGround) return false;
                        break;
                    case ProtoboardInteractor.CircuitArea.Vcc:
                        if (!canConnectToVcc) return false;
                        break;
                }
            }
    
            if (hasSpecificLimits)
            {
                foreach (Vector3Int cell in footprint)
                {
                    bool validY = cell.y <= _limitMaxY && cell.y >= _limitMinY;
                    bool validX = cell.x <= _limitMaxX && cell.x >= _limitMinX;

                    if (!validY || !validX)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // decide como a peça vai encaixar na protoboard
        public override Transform GetAttachTransform(IXRInteractor interactor)
        {
            if (interactor is ProtoboardInteractor)
            {
                //mudar transform e extends
                // pegar o attach transform definido que vai encaixar no interactor
                //  e junto vem o extends também


                return _attachPoint != null ? _attachPoint : transform;
            }
            
            return base.GetAttachTransform(interactor);
        }

        // saber quais entradas a peça vai ocupar
        public List<Vector3Int> GenerateFootprint(Grid grid)
        {
            List<Vector3Int> ocuppiedCells = new List<Vector3Int>();
            //Vector3 origin = _attachPoint != null ? _attachPoint : transform.position;
            
            /*Transform baseTransform = _attachPoint != null ? _attachPoint : 
                (attachTransform != null ? attachTransform : transform);

            // Se tiver Pivot (peça nova), usa a rotação do Pivot. Se não (peça antiga), usa a rotação normal.
            Quaternion rotacaoMatematica = pivotInterno != null ? pivotInterno.rotation : baseTransform.rotation;

            // Resolvemos o problema recriando o TransformPoint do zero, juntando as peças!
            Matrix4x4 matrizConversao = Matrix4x4.TRS(
                baseTransform.position, 
                rotacaoMatematica, 
                baseTransform.lossyScale // <-- Isso salva a vida das peças que têm tamanho diferente de 1!
            );*/
            
            Transform origin = pivotInterno != null ? pivotInterno.transform : transform;
            
            Matrix4x4 matrizConversao = Matrix4x4.TRS(
                origin.position, 
                origin.rotation, 
                origin.lossyScale // <-- Isso salva a vida das peças que têm tamanho diferente de 1!
            );

            float cellX = grid.cellSize.x + grid.cellGap.x;
            float cellY = grid.cellSize.y + grid.cellGap.y;

            for (int x = -leftExtend; x <= rightExtend; x++)
            {
                for (int y = -bottomExtend; y <= topExtend; y++)
                {
                    Vector3 offsetLocal = new Vector3(x * cellX, y * cellY, 0);
                    Vector3 worldPoint = matrizConversao.MultiplyPoint3x4(offsetLocal);
                    
                    Vector3Int cellPosition = grid.WorldToCell(worldPoint);

                    //cellPosition.z = 0;
                    if (!ocuppiedCells.Contains(cellPosition))
                    {
                        ocuppiedCells.Add(cellPosition);
                    }
                }
            }
            return ocuppiedCells;
        } 
        
        // O 'virtual' permite que classes filhas mudem ou expandam esta função
        public virtual void InstantConnect(Vector3Int pin)
        {
            ProtoboardInteractor protoboard = Object.FindFirstObjectByType<ProtoboardInteractor>();
            //Debug.Log(protoboard);
        
            if (protoboard != null && interactionManager != null)
            {
                Grid grid = protoboard.GetComponent<Grid>();
                if (grid != null)
                {
                    Vector3 pos = grid.GetCellCenterWorld(pin);
                    transform.position = pos;
                }
            
                interactionManager.SelectEnterUnconditionally(protoboard, this);
                //Debug.Log($"{gameObject.name} is connected");
            }
            else
            {
                Debug.Log("no connection found");
            }
        }
    }
}
