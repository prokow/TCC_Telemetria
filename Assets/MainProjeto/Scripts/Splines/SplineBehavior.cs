using System;
using MainProjeto.Scripts.Interactables.Multimetro;
using UnityEngine;
using UnityEngine.Splines;

namespace MainProjeto.Scripts.Splines
{
    [RequireComponent(typeof(SplineContainer))]
    public class SplineBehavior : MonoBehaviour
    {
        
        private ConnectorMultimeter _connectorInteractable;
        
        private SplineContainer _splineContainer;
        
        // Transform's para seguir o spline junto com pino
        public Transform pinA;  // lugar que o spline point[0] seguirá o pino
        public Transform pinB; // lugar que o spline point[1] seguirá para o cabo não ficar deformado
        public Transform pinC;

        private const int INDEX_PinA = 0;
        private const int INDEX_PinB = 1;
        private const int INDEX_PinC = 2;
    
        [Header("Curva do cabo Spline")] 
        public float rigidezCabo = 0.5f;
        

        private Spline _spline;
        void Start()
        {
            _connectorInteractable = GetComponentInParent<ConnectorMultimeter>();
            if (_connectorInteractable == null)
            {
                Debug.Log("not found connector interactable");
            }
            
            _splineContainer = GetComponent<SplineContainer>();
            if (!_splineContainer)
                throw new Exception("SplineBehavior needs a spline container!");

            _spline = _splineContainer.Spline;
        }


        // Função principal para atualizar o spline enquanto interage com a bateria
        // pre-condicao: index definido para qual parte do spline moverá, Transform para o cabo seguir o pino,
        //               e o booleano é importante para definir se o cabo irá mover suavemente ou não 
        // pos-condicao: retorna o movimento suave do pino quando pego e quando solto
        void UpdateSplinePoint(int index, Transform target)
        {
            if (index >= _spline.Count) return; // condição para indicar se o index existe ou não
        
            BezierKnot noKnot = _spline[index];
            Vector3 localPosition = _splineContainer.transform.InverseTransformPoint(target.position);
        
            noKnot.Position = localPosition;

            Vector3 worldTangentPos = target.forward;
            Vector3 localDirectionTangent = _splineContainer.transform.InverseTransformDirection(worldTangentPos *-Math.Abs(rigidezCabo));

            
            noKnot.Position = localPosition;
            noKnot.TangentIn = localDirectionTangent;
            
        
            noKnot.TangentOut = Vector3.zero;
            _spline.SetKnot(index, noKnot);
        }
        
        
        void LateUpdate()
        {
            
            //fará o update do spline point (Knot) [0] para o transform do Pino para seguir, e não ativa o lerp
            UpdateSplinePoint(INDEX_PinA, pinA);
            
            if (_connectorInteractable.isSelected)
            {
                // fará o update do spline point (Knot) [1] para o gameObject ao lado para nao ficar deformado o cabo
                // o lerp é ativado para ir suavemente ao lado
                UpdateSplinePoint(INDEX_PinB, pinB);
            }
            UpdateSplinePoint(INDEX_PinC, pinC);
 
        }
    }
}
    