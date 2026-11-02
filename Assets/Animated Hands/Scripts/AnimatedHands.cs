using UnityEngine;
using UnityEngine.InputSystem;

namespace Animated_Hands.Scripts
{
    public class AnimatedHands : MonoBehaviour
    {
        public InputActionProperty triggerValue;
        public InputActionProperty gripValue;
    
        private Animator _handAnimator;
    
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _handAnimator = GetComponent<Animator>();
        }

        // Update is called once per frame
        void Update()
        {
            float trigger = triggerValue.action.ReadValue<float>();
            float grip = gripValue.action.ReadValue<float>();
        
            _handAnimator.SetFloat("Trigger", trigger);
            _handAnimator.SetFloat("Grip", grip);
        }
    }
}
