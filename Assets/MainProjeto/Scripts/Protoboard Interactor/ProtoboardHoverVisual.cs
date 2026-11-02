using System;
using System.Collections.Generic;
using MainProjeto.Scripts.Interactables.Multimetro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MainProjeto.Scripts.Protoboard_Interactor
{
    [RequireComponent(typeof(ProtoboardInteractor), typeof(Grid))]
    public class ProtoboardHoverVisual : MonoBehaviour
    {
        private Grid _grid;
        private ProtoboardInteractor _interactor;
    
        [Header("Visual Settings")]
        public Material hoverMeshMaterial;
        public Material invalidHoverMeshMaterial;
        public Material multimeterMeshMaterial;
        public bool showHoverMesh = true;
    
        // onde será guardado o mesh que esta sendo colocado para desenhar depois
        private Dictionary<IXRInteractable, ValueTuple<MeshFilter, Renderer>[]> m_MeshFilterCache = new Dictionary<IXRInteractable, ValueTuple<MeshFilter, Renderer>[]>();
        private static readonly List<MeshFilter> s_MeshFilters = new List<MeshFilter>();
    
        protected void Awake()
        {
            _interactor = GetComponent<ProtoboardInteractor>();
            _grid = GetComponent<Grid>();
        }


        private void OnEnable()
        {
            _interactor.hoverEntered.AddListener(OnHoverEntering);
            _interactor.hoverExited.AddListener(OnHoverExiting);
        }

        private void OnDisable()
        {
            _interactor.hoverEntered.RemoveListener(OnHoverEntering);
            _interactor.hoverExited.RemoveListener(OnHoverExiting);
        }

        private void OnHoverEntering(HoverEnterEventArgs args)
        {
            if (!showHoverMesh || hoverMeshMaterial == null) return;
        
            var interactable = args.interactableObject;
            s_MeshFilters.Clear();
            interactable.transform.GetComponentsInChildren<MeshFilter>(true, s_MeshFilters);
        
            if (s_MeshFilters.Count == 0) return;
        
            var interactableTuples = new ValueTuple<MeshFilter, Renderer>[s_MeshFilters.Count];

            for (var i = 0; i < s_MeshFilters.Count; i++)
            {
                var meshFilter = s_MeshFilters[i];
                interactableTuples[i] = (meshFilter, meshFilter.GetComponent<Renderer>());
            }
            m_MeshFilterCache.Add(interactable, interactableTuples);
        }

        private void OnHoverExiting(HoverExitEventArgs args)
        {
            m_MeshFilterCache.Remove(args.interactableObject);
        }

        private void LateUpdate()
        {
            if (showHoverMesh && _interactor.hasHover && hoverMeshMaterial != null)
            {
                DrawHoverMeshes();
            }
        }
    
        private void DrawHoverMeshes()
        {
            foreach (var interactable in _interactor.interactablesHovered)
            {
                if (interactable is IXRSelectInteractable selectInteractable && _interactor.IsSelecting(selectInteractable)) continue;
            
                if(!m_MeshFilterCache.TryGetValue(interactable, out var interactableTuples)) continue;

               //bool isPlacementValid = _interactor.IsPlacementValid(interactable);
            
                //Material currentMaterial = isPlacementValid ? hoverMeshMaterial : invalidHoverMeshMaterial;
            
                bool isPlacementValid = _interactor.IsPlacementValid(interactable);
        
                Material currentMaterial = invalidHoverMeshMaterial; // Assume vermelho por defeito

                // 2. Lógica Específica para a Cor:
                if (isPlacementValid)
                {
                    if (interactable is ConnectorMultimeter conector)
                    {
                        // Se é um conector, pergunta à Protoboard se aquele furo exato aciona o Multímetro
                        // Nota: Teremos que criar esta função rápida na Protoboard a seguir
                        if (_interactor.IsMeasurementCellValid(conector))
                        {
                            currentMaterial = multimeterMeshMaterial; // Verde se acertar no furo da medição
                        }
                        else
                        {
                            currentMaterial = multimeterMeshMaterial; // Vermelho se for num furo errado
                        }
                    }
                    else
                    {
                        // Se é um LED, Resistor, etc., e cabe na placa, fica sempre verde (livre exploração)
                        currentMaterial = hoverMeshMaterial; 
                    }
                }
                else
                {
                    currentMaterial = invalidHoverMeshMaterial;
                }
                
                Vector3 attachPosition = interactable.GetAttachTransform(_interactor).position;
            
                Vector3Int cell = _grid.WorldToCell(attachPosition);
                Vector3 targetCellPosition = _grid.GetCellCenterWorld(cell);

                Vector3 positionOffset = targetCellPosition - attachPosition;
            
                foreach (var tuple in interactableTuples)
                {
                    var meshFilter = tuple.Item1;
                    var meshRenderer = tuple.Item2;

                    if (meshFilter == null || meshRenderer == null || !meshRenderer.enabled) continue;
                
                    Vector3 virtualPosition = meshFilter.transform.position + positionOffset;

                    Matrix4x4 matrix = Matrix4x4.TRS(virtualPosition, meshFilter.transform.rotation,
                        meshFilter.transform.lossyScale);
                
                    var sharedMesh = meshFilter.sharedMesh;
                    for (var subMeshIndex = 0; subMeshIndex < sharedMesh.subMeshCount; ++subMeshIndex)
                    {
                        Graphics.DrawMesh(
                            sharedMesh,
                            matrix,
                            currentMaterial,
                            gameObject.layer,
                            null,
                            subMeshIndex);
                    }
                }
            }
        }
    }
}