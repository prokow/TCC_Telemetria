using UnityEngine;

namespace MainProjeto.Scripts.Protoboard_Interactor
{
    [RequireComponent(typeof(Grid))]
    public class GridDebugger : MonoBehaviour
    {
        private Grid _grid;

        private BoxCollider boxCollider;
    
        [Header("Configurações de Visualização")]
        [SerializeField]
        private bool showGrid = true;
        [SerializeField]
        private Color gridColor = new Color(0f, 1f, 1f, 0.5f);
        [SerializeField]
        private bool showCenterDot = true;
    

        private void OnDrawGizmos()
        {
            if (!showGrid) return;

            if(boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        

            if (boxCollider.bounds.size == Vector3.zero)
            {
                Debug.LogWarning("BoxCollider tem tamanho zero (0,0,0)!");
                return;
            }
        
            if (_grid == null) _grid = GetComponent<Grid>();
        
            if(_grid == null)
            {
                Debug.LogWarning("Component Grid nao encontrado!");
            }

            Gizmos.color = gridColor;
      
            Bounds bounds = boxCollider.bounds;

            // Pergunta ao Grid em quais células essas pontas caem
            Vector3Int minCell = _grid.WorldToCell(bounds.min);
            Vector3Int maxCell = _grid.WorldToCell(bounds.max);

            // Faz o loop exatamente dentro desse limite, nem um furo a mais, nem a menos
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int z = minCell.z; z <= maxCell.z; z++)
                    {
                        Vector3Int cellPos = new Vector3Int(x, y, z);
                        Vector3 worldPos = _grid.GetCellCenterWorld(cellPos);

                        // desenha se o centro do furo estiver dentro da caixa 
                        if (bounds.Contains(worldPos))
                        {
                            Gizmos.DrawWireCube(worldPos, _grid.cellSize);
                        
                            if (showCenterDot)
                            {
                                Gizmos.DrawSphere(worldPos, _grid.cellSize.magnitude * 0.05f);
                            }
                        }
                    } 
                }
            }
        }
    }
}