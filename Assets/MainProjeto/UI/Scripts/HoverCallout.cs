using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MainProjeto.UI.Scripts
{
    public class HoverCallout : MonoBehaviour
    {
        [SerializeField] 
        private XRBaseInteractable interactable;
        [SerializeField] 
        private GameObject calloutPrefab;
        [SerializeField] 
        private float heightOffset = 0.3f;
        [SerializeField] 
        private float spawnAnimationDuration = 0.2f;
        [SerializeField]
        private bool updatePositionAlways;
        [SerializeField]
        private CalloutPositionMode positionMode;
        [SerializeField]
        private float lookDirectionOffset = 0f;
        [SerializeField]
        private InteractionLayerMask triggerLayers = ~0;

        private enum CalloutPositionMode { Interactable, InteractionPoint }

        private GameObject _calloutInstance;
        private Camera _camera;
        private int _hoverCount;
        private Vector3 _calloutOriginalScale;
        private Vector3 _scaleTarget;
        private Vector3 _scaleVelocity;
        private IXRInteractor _currentInteractor;

        private bool _isSelected;

        private void Awake()
        {
            if (!interactable)
                interactable = GetComponent<XRBaseInteractable>();

            if (!interactable || !calloutPrefab)
                Debug.LogError($"[HoverCallout - {gameObject.name}] ERRO: Faltando Interactable ou Callout Prefab no Inspector!");
            
            _camera = Camera.main;
            if (_camera == null)
                Debug.LogWarning($"[HoverCallout - {gameObject.name}] AVISO: Câmera principal não encontrada. Verifique se a sua câmera XR tem a tag 'MainCamera'.");
            _calloutOriginalScale = calloutPrefab.transform.localScale;
        }

        private void Start()
        {
            _calloutInstance = Instantiate(calloutPrefab);
            _calloutInstance.transform.position = transform.position + Vector3.up * heightOffset;
            _calloutInstance.transform.localScale = Vector3.zero;
            _scaleTarget = Vector3.zero;
            _calloutInstance.SetActive(false);
            
        }

        private void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {

            if ((args.interactorObject.interactionLayers & triggerLayers) == 0)
            {
                return;
            }

            _hoverCount++;
            if (_hoverCount == 1)
            {
                _currentInteractor = args.interactorObject;
                _calloutInstance.transform.position = GetCalloutPosition();
                _calloutInstance.SetActive(true);

                if (!_isSelected)
                {
                    _scaleTarget = _calloutOriginalScale;
                }
            }
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if ((args.interactorObject.interactionLayers & triggerLayers) == 0)
                return;

            _hoverCount--;
            if (_hoverCount <= 0)
            {
                _currentInteractor = null;
                _scaleTarget = Vector3.zero;
                _hoverCount = 0;
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // Esconde o texto APENAS se quem pegou for a mão do jogador (Raio ou Contato Direto)
            if (args.interactorObject is XRDirectInteractor || args.interactorObject is XRRayInteractor)
            {
                _isSelected = true;
                _scaleTarget = Vector3.zero;
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            // Restaura a lógica APENAS se quem soltou foi a mão do jogador
            if (args.interactorObject is XRDirectInteractor || args.interactorObject is XRRayInteractor)
            {
                _isSelected = false;
                if (_hoverCount > 0)
                {
                    _scaleTarget = _calloutOriginalScale;
                }
            }
        }

        
        private Vector3 GetCalloutPosition()
        {
            Vector3 lookOffset = _camera != null
                ? Vector3.ProjectOnPlane(_camera.transform.position - transform.position, Vector3.up).normalized * lookDirectionOffset
                : Vector3.zero;

            if (positionMode == CalloutPositionMode.InteractionPoint && _currentInteractor != null)
                return interactable.GetAttachTransform(_currentInteractor).position + Vector3.up * heightOffset + lookOffset;

            return transform.position + Vector3.up * heightOffset + lookOffset;
        }

        private void LateUpdate()
        {
            if (!_calloutInstance.activeSelf || _camera == null) return;
            if (updatePositionAlways)
                _calloutInstance.transform.position = GetCalloutPosition();

            Vector3 toCamera = _camera.transform.position - _calloutInstance.transform.position;
            _calloutInstance.transform.rotation = Quaternion.LookRotation(-toCamera);

            Vector3 currentScale = _calloutInstance.transform.localScale;
            if ((currentScale - _scaleTarget).sqrMagnitude > 0.0000001f)
            {
                _calloutInstance.transform.localScale = Vector3.SmoothDamp(
                    currentScale, _scaleTarget, ref _scaleVelocity, spawnAnimationDuration);
            }
            else
            {
                _calloutInstance.transform.localScale = _scaleTarget;
                _scaleVelocity = Vector3.zero;
                if (_scaleTarget == Vector3.zero)
                    _calloutInstance.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_calloutInstance)
                Destroy(_calloutInstance.gameObject);
        }
    }
}