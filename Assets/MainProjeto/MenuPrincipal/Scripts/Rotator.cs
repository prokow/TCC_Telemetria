using UnityEngine;

namespace MainProjeto.MenuPrincipal.Scripts
{
    public class Rotator : MonoBehaviour
    {
   
        [SerializeField]
        Vector3 rotacao = new Vector3(0, 0, 0);    
    
        // Update is called once per frame
        void Update()
        {
            gameObject.transform.Rotate(rotacao * (Time.deltaTime * 10.0f));
        }
    }
}
