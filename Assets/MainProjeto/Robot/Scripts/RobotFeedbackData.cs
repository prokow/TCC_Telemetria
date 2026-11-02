using System.Collections.Generic;
using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    [CreateAssetMenu(fileName = "NewRobotFeedback", menuName = "Feedback/Robot Feedback")]
    public class RobotFeedbackData : ScriptableObject
    {
        public new string name;
    
        [Header("Efeito Sonoro do Robo")] 
        public AudioClip sound;

        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.1f, 3f)] public float pitch = 1f;

        [Header("Configurações do Texto do Robo")] 
        public string ballonText;
        public float ballonDisplayTime = 3f;
    
        [Header("Animações do Robo")]
        public List<string> animationTriggers = new List<string>();
    }
}
