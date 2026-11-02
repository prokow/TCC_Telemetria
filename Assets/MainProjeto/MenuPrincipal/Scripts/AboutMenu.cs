using QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MainProjeto.MenuPrincipal.Scripts
{
    public class AboutMenu : XRSimpleInteractable
    {
        private Outline _outline;
    
        [Header("Canvas da Janela de Sobre do Menu")]
        [SerializeField]
        private GameObject _aboutMenu;
    
        private bool isPressed = false;

        protected override void Awake()
        {
            base.Awake();
        
            _aboutMenu.SetActive(false);
            _outline = GetComponent<Outline>();
            _outline.enabled = false;
        }

        protected override void OnHoverEntered(HoverEnterEventArgs args)
        {
            base.OnHoverEntered(args);
            if (_outline != null)
            {
                _outline.OutlineMode = Outline.Mode.OutlineAll;
                _outline.OutlineWidth = 9.0f;
                _outline.OutlineColor = Color.yellow;
                _outline.enabled = true;
            }
        }

        protected override void OnHoverExited(HoverExitEventArgs args)
        {
            base.OnHoverExited(args);
            if (_outline != null)
            {
                _outline.enabled = false;
            }
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
        
            bool pressed = isPressed ? true : false;
        
            if (!pressed)
            {
                _aboutMenu.SetActive(true);
                isPressed = true;
                Debug.Log("ligado");
            }
            else
            {
                _aboutMenu.SetActive(false);
                isPressed = false;
                Debug.Log("desligado");
            }
        
        }
    }
}
