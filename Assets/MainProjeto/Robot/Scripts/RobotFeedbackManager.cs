using System;
using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    public class RobotFeedbackManager : MonoBehaviour
    {
    
        public static RobotFeedbackManager Instance { get; private set; }
        public event Action<RobotFeedbackData> OnFeedbackTrigger;
        public static event Action<float> OnMaterialFeedback;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
        }

        // ReSharper disable Unity.PerformanceAnalysis
        public void TriggerFeedback(RobotFeedbackData feedbackData)
        {
            if (feedbackData == null) return;
        
            OnFeedbackTrigger?.Invoke(feedbackData);
            OnMaterialFeedback?.Invoke(feedbackData.sound.length);
        }
    }
}
