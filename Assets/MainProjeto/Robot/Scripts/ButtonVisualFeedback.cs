using System.Collections;
using QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MainProjeto.Robot.Scripts
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(Outline))]
    public class ButtonVisualFeedback : XRSimpleInteractable
    {
        [Header("Tempo de Feedback")]
        [SerializeField] private float tempoPressionado = 0.3f;

        private Renderer _renderer;
        private Animator _animator;
        private Outline _outline;
        private Coroutine _pressRoutine;

        private bool _isAnimating = false;

        protected override void Awake()
        {
            base.Awake();
            
            _outline = GetComponent<Outline>();
            if (_outline != null)
            {
                _outline.OutlineMode = Outline.Mode.OutlineAll;
                _outline.OutlineWidth = 8f;
                _outline.OutlineColor = Color.yellow;
                _outline.enabled = false;
            }
            
            _animator = GetComponent<Animator>();
            _renderer = GetComponent<Renderer>();
        }
        

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            return !_isAnimating && base.IsSelectableBy(interactor);
        }
        
        

        protected override void OnHoverEntered(HoverEnterEventArgs args)
        { 
            base.OnHoverEntered(args); 
            if (_outline != null) _outline.enabled = true; 
        }

        protected override void OnHoverExited(HoverExitEventArgs args)
        { 
            base.OnHoverExited(args); 
            if (_outline != null) _outline.enabled = false; 
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            if(_isAnimating) return;
            
            base.OnSelectEntered(args);
            
            if (_pressRoutine != null) StopCoroutine(_pressRoutine);
            _pressRoutine = StartCoroutine(PressRoutine());
        }

        private IEnumerator PressRoutine()
        {
            _isAnimating = true;
            
            if (_animator != null) 
            {
                _animator.SetTrigger("Press");
            }
            
            if (_outline != null)
            {
                _outline.enabled = false;
            }

            yield return new WaitForSeconds(tempoPressionado);
            
            _isAnimating = false;
            
            if (isHovered && _outline != null) 
            {
                _outline.enabled = true;
            }
        }
    }
}