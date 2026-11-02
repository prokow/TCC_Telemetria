using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    public class RobotSoundController : MonoBehaviour
    {
        private AudioSource _audioSource;
        private RobotFeedbackManager _manager;
        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        
            _manager = GetComponentInParent<RobotFeedbackManager>();
            _audioSource.spatialBlend = 1f;
        }

        private void OnEnable() => _manager.OnFeedbackTrigger += PlaySoundEvent;

        private void OnDisable() => _manager.OnFeedbackTrigger -= PlaySoundEvent;

        private void PlaySoundEvent(RobotFeedbackData data)
        {
            if (data.sound == null) return;

            _audioSource.clip = data.sound;
            _audioSource.volume = data.volume;
            _audioSource.pitch = data.pitch;
            _audioSource.Play();
        }
    }
}
