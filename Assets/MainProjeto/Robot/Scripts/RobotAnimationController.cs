using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    [RequireComponent(typeof(Animator))]
    public class RobotAnimationController : MonoBehaviour
    {
        private Animator animator;
        private RobotFeedbackManager _manager;

        private void Awake()
        {
            animator = GetComponent<Animator>();
        
            _manager = GetComponentInChildren<RobotFeedbackManager>();
        }

        private void OnEnable()
        {
            if (_manager != null)
            {
                _manager.OnFeedbackTrigger += PlayAnimation;
            }
        }

        private void OnDisable()
        {
            if (_manager != null)
            {
                _manager.OnFeedbackTrigger -= PlayAnimation;
            }
        }

        private void PlayAnimation(RobotFeedbackData data)
        {
            if (data.animationTriggers != null && data.animationTriggers.Count > 0)
            {
                int layerIndex = 0;
            
                foreach (string triggerName in data.animationTriggers)
                {
                    Debug.Log(triggerName + " triggered");
                    if (!string.IsNullOrEmpty(triggerName))
                    {
                        animator.CrossFade(triggerName, 0.2f);
                        Debug.Log(triggerName);

                        layerIndex++;
                    }
                }
            }
        }
    }
}
