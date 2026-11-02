using System.Collections;
using System.Collections.Generic;
using MainProjeto.Robot.Scripts;
using QuickOutline.Scripts;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MainProjeto.Scripts.Protoboard_Interactor
{
    [RequireComponent(typeof(Outline))]
    public class CircuitoFlowButton : XRSimpleInteractable
    {
        private Outline _outline;
        private Animator _animator;
        private Renderer _renderer;
        private bool isPressed = false;
        private bool _hasPlayed = false;
    
        [Header("Material's para se pressionar ou não")]
        [SerializeField] private Material _materialPressed;
        [SerializeField] private Material _materialUnpressed;

        [Header("Canvas a ser mostrado do Flow do Circuito")] 
        [SerializeField]
        private GameObject _gameObject;
    
        [Header("Sequência de Falas ao Ligar")]
        [SerializeField] 
        private List<RobotFeedbackData> sequenciaAoLigar = new List<RobotFeedbackData>();
    
        private Coroutine _sequenceCoroutine;
    
        protected override void Awake()
        {
            base.Awake();

            if (_gameObject == null)
            {
                Debug.LogWarning("Circuito Flow - GameObject nao definido");
            }
            _gameObject.SetActive(false);
        
            _outline = GetComponent<Outline>();
            if (_outline != null)
            {
                _outline.OutlineMode = Outline.Mode.OutlineAll;
                _outline.OutlineWidth = 8.0f;
                _outline.OutlineColor = Color.darkTurquoise;
                _outline.enabled = false;
            }
        
            _animator = GetComponent<Animator>();
            _renderer = GetComponent<Renderer>();

            if (_renderer != null && _materialUnpressed != null)
            {
                _renderer.material = _materialUnpressed;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (RobotFeedbackManager.Instance != null)
            {
                RobotFeedbackManager.Instance.OnFeedbackTrigger += HandleExternalInterrupt;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (RobotFeedbackManager.Instance != null)
            {
                RobotFeedbackManager.Instance.OnFeedbackTrigger -= HandleExternalInterrupt;
            }
        }

        private void HandleExternalInterrupt(RobotFeedbackData data)
        {
            // Se a fala disparada NÃO for uma das 4 falas do botão, ele cala a boca
            if (!sequenciaAoLigar.Contains(data) && _sequenceCoroutine != null)
            {
                StopCoroutine(_sequenceCoroutine);
                _sequenceCoroutine = null;
            }
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

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if(_animator != null) _animator.SetTrigger("Press");

            isPressed = !isPressed;
        
            if(_renderer != null)
            {
                if (isPressed)
                {
                    _renderer.material = _materialPressed;
                    if(_gameObject != null) _gameObject.SetActive(true);
                
                    if(!_hasPlayed && sequenciaAoLigar.Count > 0)
                    {
                        _sequenceCoroutine = StartCoroutine(PlaySequence());
                        _hasPlayed = true;
                    } 
                }
                else
                {
                    _renderer.material = _materialUnpressed;
                    if(_gameObject != null) _gameObject.SetActive(false);
                
                    if(_sequenceCoroutine != null)
                    {
                        StopCoroutine(_sequenceCoroutine);
                        _sequenceCoroutine = null;
                    }
                }
            }
        }

        private IEnumerator PlaySequence()
        {
            for (int i = 0; i < sequenciaAoLigar.Count; i++)
            {
                RobotFeedbackData feedback = sequenciaAoLigar[i];

                if (RobotFeedbackManager.Instance != null && feedback != null)
                {
                    RobotFeedbackManager.Instance.TriggerFeedback(feedback);

                    bool isLastItem = (i == sequenciaAoLigar.Count - 1);

                    if (!isLastItem)
                    {
                        yield return new WaitForSeconds(feedback.ballonDisplayTime);
                    }
                }
            }
        }

    }
}
