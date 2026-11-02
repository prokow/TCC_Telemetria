using System.Collections.Generic;
using MainProjeto.Scripts.Interactables.Multimetro;
using MainProjeto.Scripts.ParentCoponent;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MainProjeto.Scripts.Protoboard_Interactor
{
    [RequireComponent(typeof(Grid))]
    public class ProtoboardInteractor : XRBaseInteractor
    {
        private Grid _grid;
    
        // determina quais interactables estão sendo usados na protoboard
        private List<IXRInteractable> _grabInteractables = new List<IXRInteractable>();
    
        // determinar os attach points de cada interactable colocado na protoboard
        private Dictionary<IXRInteractable, Transform> _dynamicAttachPoints = new Dictionary<IXRInteractable, Transform>();

        private readonly ProtoboardOccupancyMap<IXRInteractable> _occupiedCells = new();
        private readonly Dictionary<IXRInteractable, HashSet<Collider>> _contactColliders = new();
    
    
        protected override void Awake()
        {
            base.Awake();
            _grid = GetComponent<Grid>();
        }

        
        
        
        // feito a consulta do attach transform enquanto a interação está acontecendo
        public override Transform GetAttachTransform(IXRInteractable interactable)
        {
            //return interactable.transform;

            if (!_dynamicAttachPoints.TryGetValue(interactable, out Transform attachPoint))
            {
                GameObject attach = new GameObject($"Attach Transform_{interactable.transform.name}");
                attach.transform.SetParent(transform, false);
                attachPoint = attach.transform;
                _dynamicAttachPoints[interactable] = attachPoint;
            }
        
        
            Vector3Int cell = _grid.WorldToCell(interactable.transform.position);
            cell.z = 0;
            Vector3 snapPos = _grid.GetCellCenterWorld(cell);
            attachPoint.position = snapPos;
       
            
            return attachPoint;
        }


        public List<Vector3Int> GetTargetCells(IXRInteractable interactable)
        {
            List<Vector3Int> cells = new List<Vector3Int>();
        
            if (interactable is EletronicComponent component)
            {
                return component.GenerateFootprint(_grid);
            }
        
            List<Vector3Int> singleCell = new List<Vector3Int>();
            singleCell.Add(_grid.WorldToCell(interactable.transform.position));
            return singleCell;
        }

        private bool AreCellsFree(List<Vector3Int> targetCells, IXRInteractable interactable)
        {
            foreach (var cell in targetCells)
            {
                if (GetCircuitArea(_grid.GetCellCenterWorld(cell)) == CircuitArea.None)
                {
                    return false;
                }
            
                if (!_occupiedCells.IsFree(cell, interactable, target => target is Object oldObject && oldObject == null)) return false;
            }

            return true; // entradas livres
        }
    
        //-------------------//-------------------//-------------------//-------------------
    
        public enum CircuitArea
        {
            None,
            Vcc,
            Ground
        }
    
        public CircuitArea GetCircuitArea(Vector3 worldPos)
        {
            Vector3Int cell = _grid.WorldToCell(worldPos);
        
            if (IsCellValidVcc(cell)) return CircuitArea.Vcc;
            if (IsCellValidGround(cell)) return CircuitArea.Ground;

            return CircuitArea.None;
        }
    
        public bool IsPlacementValid(IXRInteractable interactable)
        {
            List<Vector3Int> targetCells = GetTargetCells(interactable);

            if (!AreCellsFree(targetCells, interactable)) return false;

            if (interactable is EletronicComponent comp)
            {
                if (!comp.ValidatePins(this)) return false;
            }
        
            return true;
        }

    

        //-------------------//-------------------//-------------------//-------------------
    
        private bool IsCellValidGround(Vector3Int cell)
        {
            int ground = ((cell.x % 6) + 6) % 6;
        
            //Eixo Y
            if (cell.y == 9 || cell.y == 8 || cell.y == -9 || cell.y == -10)
            {
                //Debug.LogWarning("Ground -> y: " + cell.y);
            
                if (cell.x >= 30 || cell.x <= -32)
                {
                    return false;   
                }

                if (ground == 5)
                {
                    return false;
                }
            
                return true;
            }

            return false;
        }
    
    
        private bool IsCellValidVcc(Vector3Int cell)
        {
            //Debug.Log("z: " + cell.z);
            if (cell.z != 0)
            {
                return false;
            }
        
            if(cell.y >= 1 && cell.y <= 5)
            {
                //Debug.Log("Tabela A-E -> y: " + cell.y);
                //Debug.Log("Tabela A-E -> x: " + cell.x);
                
                if (cell.x > 32 || cell.x < -32)
                {
                    return false;
                }
            
                return true;
            }
        
            if(cell.y <= -2 && cell.y >= -6)
            {
                //Debug.Log("Tabela F-J -> y: " + cell.y);
                //Debug.Log("Tabela F-J -> x: " + cell.x);

                if (cell.x > 32 || cell.x < -32)
                {
                    return false;
                }
            
                return true;
            }
        
            return false;
        }

    
        /* 9
     * 8
     * 1 até 5
     * -2 até -8
     * -9
     * -10
     */

        public bool IsMeasurementCellValid(ConnectorMultimeter connector)
        {
            List<Vector3Int> cells = GetTargetCells(connector);
            if (cells == null || cells.Count == 0) return false;

            Vector3Int currentHoverCell = cells[0];

            // Comunica diretamente com a classe, sem precisar de referências no Inspector
            return MeasurerMultimeter.CheckIfAnyMeasurerAccepts(connector.GetConnectorType(), currentHoverCell); 
        }
        
    
        public override void GetValidTargets(List<IXRInteractable> targets)
        {
            targets.Clear();
            for (int i = _grabInteractables.Count - 1; i >= 0; i--)
            {
                var target = _grabInteractables[i];
                bool remove = target == null || (target is Object unityTarget && unityTarget == null);
                if (!remove)
                {
                    remove = !target.transform.gameObject.activeInHierarchy;
                    if (_contactColliders.TryGetValue(target, out var contacts))
                    {
                        contacts.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
                        remove |= contacts.Count == 0;
                    }
                }
                if (remove) ForgetTarget(target);
            }
            targets.AddRange(_grabInteractables); //vai adicionar os items que poderemos interagir neste interactor
        }

        public override bool CanSelect(IXRSelectInteractable interactable)
        {
            if (!base.CanSelect(interactable)) return false;
            if (IsSelecting(interactable)) return true;
            if (interactable.isSelected) return false;

            List<Vector3Int> targetCells = GetTargetCells(interactable);
        
            return AreCellsFree(targetCells, interactable);
            //return IsPlacementValid(interactable);
        }

        public override bool CanHover(IXRHoverInteractable interactable)
        {
            return !IsSelecting(interactable);
        }
    
        // é necessário colocar ativar o IsTrigger para que o componente identifique
        // que está colidindo com algum objeto, e que ele seja XRGrabInteractable para ser
        // adicionado a lista
        private void OnTriggerEnter(Collider other)
        {
            XRGrabInteractable interactable = other.GetComponentInParent<XRGrabInteractable>();

            if (interactable != null)
            {
                if (!_contactColliders.TryGetValue(interactable, out var contacts))
                {
                    contacts = new HashSet<Collider>();
                    _contactColliders[interactable] = contacts;
                }
                contacts.Add(other);
                //Debug.Log(grabInteractables.Contains(interactable));
                //Vector3Int cellEntered = _grid.WorldToCell(other.transform.position);
                if (!_grabInteractables.Contains(interactable))
                {
                    _grabInteractables.Add(interactable);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            XRGrabInteractable interactable = other.GetComponentInParent<XRGrabInteractable>();
        
            if (interactable != null)
            {
                if (_contactColliders.TryGetValue(interactable, out var contacts))
                {
                    contacts.Remove(other);
                    if (contacts.Count == 0) ForgetTarget(interactable);
                }
            }
        }
    

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            var interactable = args.interactableObject;
        
        
            List<Vector3Int> targetCells = GetTargetCells(interactable);
            _occupiedCells.Reserve(interactable, targetCells);
        }


        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
        
            var interactable = args.interactableObject;
            /*if (interactable is EletronicComponent eletronicComponent)
        {
            //eletronicComponent.exte
        }*/
            ReleaseOccupiedCells(interactable);
            if (!_contactColliders.ContainsKey(interactable)) RemoveAttachPoint(interactable);
        }

        private void ReleaseOccupiedCells(IXRInteractable interactable)
        {
            _occupiedCells.Release(interactable);
        }

        private void ForgetTarget(IXRInteractable target)
        {
            _grabInteractables.Remove(target);
            if (target == null) return;
            _contactColliders.Remove(target);
            if ((target is Object unityTarget && unityTarget == null) || !(target is IXRSelectInteractable selectable) || !IsSelecting(selectable))
            {
                ReleaseOccupiedCells(target);
                RemoveAttachPoint(target);
            }
        }

        private void RemoveAttachPoint(IXRInteractable target)
        {
            if (!_dynamicAttachPoints.TryGetValue(target, out var point)) return;
            _dynamicAttachPoints.Remove(target);
            if (point == null) return;
            if (Application.isPlaying) Destroy(point.gameObject);
            else DestroyImmediate(point.gameObject);
        }

        protected override void OnDisable()
        {
            base.OnDisable(); // O XR cancela as selecoes e dispara OnSelectExited.
            _grabInteractables.Clear(); _contactColliders.Clear();
            _occupiedCells.Clear();
            foreach (var target in new List<IXRInteractable>(_dynamicAttachPoints.Keys)) RemoveAttachPoint(target);
        }

        // ---- Gizmos ----
        private void OnDrawGizmos()
        {
            if (_grid == null) _grid = GetComponent<Grid>();
            if (_grid == null) return;

            // Mostrar os buracos OCUPADOS (Vermelho)
            if (interactablesSelected != null)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f); 
                foreach (var interactable in interactablesSelected)
                {
                    DrawFootprintCells(interactable);
                }
            }

            // Mostra a entrada da peça sendo segurada (Amarelo)
            if (interactablesHovered != null)
            {
                foreach (var interactable in interactablesHovered)
                {
                    if (!IsSelecting(interactable))
                    {
                        bool canPlace = IsPlacementValid(interactable);

                        Gizmos.color = canPlace ? new Color(0.1f, 1f, 0.1f, 0.6f) : 
                            new Color(1f, 0f, 0.016f, 0.8f);
                    
                        DrawFootprintCells(interactable);
                    }
                }
            }
        }
    
        // Método que decide como desenhar com base no tipo da peça
        void DrawFootprintCells(IXRInteractable interactable)
        {
            // Tenta descobrir se a peça tem múltiplos pinos (O nosso novo script)
            if(interactable is EletronicComponent component)
            {
                List<Vector3Int> footprint = component.GenerateFootprint(_grid);

                foreach (Vector3Int cell in footprint)
                {
                    Vector3 worldPos = _grid.GetCellCenterWorld(cell);
                    DrawSingleCell(worldPos);
                }
            }
            else
            {
                Transform attachPoint = interactable.GetAttachTransform(this);
                Vector3 targetPos = attachPoint != null ? attachPoint.position : interactable.transform.position;
                DrawSingleCell(targetPos);
            }
        }

        // desenha o quadrado
        void DrawSingleCell(Vector3 targetWorldPosition)
        {
            Vector3Int cell = _grid.WorldToCell(targetWorldPosition);
            Vector3 cellCenterPos = _grid.GetCellCenterWorld(cell);

            Color fillColor = Gizmos.color;
            Color edgeColor = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);

            Gizmos.color = fillColor;
            Gizmos.DrawCube(cellCenterPos, _grid.cellSize);

            Gizmos.color = edgeColor;
            Gizmos.DrawWireCube(cellCenterPos, _grid.cellSize);

            Gizmos.DrawSphere(cellCenterPos, 0.001f);

            // Restaura a cor original para não bugar o próximo desenho no loop
            Gizmos.color = fillColor;
        }
    }
}
