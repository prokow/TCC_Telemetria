using UnityEngine;

namespace MainProjeto.UI.Indicator.Scripts
{
    public class UpdateLookAtArrow : MonoBehaviour
    {
        private Transform m_camTransform;
        private Vector3 m_startPosition;
    
        [Header("Booleano que autoriza se a seta se moverá ou não")]
        [SerializeField] private bool m_Animation = true;
    
        [Header("Configurações da Animação da Seta")]
        public float m_height = 0.01f;
        public float m_speed = 4.0f;
    
        private void Start()
        {
            if (Camera.main.transform != null) m_camTransform = Camera.main.transform;
        
            m_startPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (m_camTransform == null) return;
            
            if(m_Animation){
                float offset = Mathf.Sin(Time.time * m_speed) * m_height;                
                float eixoY = m_startPosition.y + offset;
                transform.position = new Vector3(transform.position.x, eixoY, transform.position.z);
            }
            else
            {
                transform.position = m_startPosition;
            }
            
            Vector3 target = new Vector3(m_camTransform.position.x, transform.position.y, m_camTransform.position.z);
            transform.LookAt(target); 
        }
    }
}
