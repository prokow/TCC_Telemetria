using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MainProjeto.Robot.Scripts
{
    [RequireComponent(typeof(XRBaseInteractable))]
    public class FeedbackOnGrab : MonoBehaviour
    {
        [SerializeField]
        private RobotFeedbackData feedbackManager;

        [SerializeField] private bool playOnlyOnce = true;
        
        private XRBaseInteractable _interactable;
        private bool _hasPlayed = false;
        
        
        
        private void Awake()
        {
            _interactable = GetComponent<XRBaseInteractable>();
        }

        private void OnEnable() => _interactable.selectEntered.AddListener(OnGrab);

        private void OnDisable() => _interactable.selectEntered.RemoveListener(OnGrab);


        private void OnGrab(SelectEnterEventArgs args)
        {
            if (playOnlyOnce && _hasPlayed) return;

            if (RobotFeedbackManager.Instance != null && feedbackManager != null)
            {
                RobotFeedbackManager.Instance.TriggerFeedback(feedbackManager);
                _hasPlayed = true;
            }
            
        }
    }
}