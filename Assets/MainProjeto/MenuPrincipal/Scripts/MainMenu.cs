using UnityEngine;

namespace MainProjeto.MenuPrincipal.Scripts
{
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] private MenuIntegrationController integration;

        public void IniciarSimulador()
        {
            if (integration == null) integration = GetComponent<MenuIntegrationController>();
            if (integration != null) integration.StartExperience();
            else Debug.LogWarning("[MainMenu] Configure a seleção de participantes antes de iniciar.");
        }

        public void Sair()
        {
            Application.Quit();
        }
    
    }
}
