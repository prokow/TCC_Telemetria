using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    public class RobotUIController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private GameObject ballonPrefab;
        [SerializeField] private Transform ballonSpawnTransform;
    
        private GameObject _currentBallon;
        private RobotFeedbackManager _manager;
    
        private void Awake()
        {
            _manager = GetComponentInParent<RobotFeedbackManager>();
        }

        private void OnEnable() => _manager.OnFeedbackTrigger += ShowBallon;
        private void OnDisable() => _manager.OnFeedbackTrigger -= ShowBallon;

        private void ShowBallon(RobotFeedbackData data){
       
            if(ballonPrefab != null && ballonSpawnTransform != null)
            {
                if (_currentBallon != null)
                {
                    Destroy(_currentBallon);
                }

                _currentBallon = Instantiate(ballonPrefab, ballonSpawnTransform.position, Quaternion.identity);
                _currentBallon.transform.SetParent(ballonSpawnTransform);

                BallonText ballonText = _currentBallon.GetComponentInChildren<BallonText>();
                if (ballonText != null)
                {
                    //Debug.Log($"<color=green>{data.ballonText} </color>");
                    ballonText.Setup(data.ballonText, data.ballonDisplayTime);
                }
            }
        }
    }
}