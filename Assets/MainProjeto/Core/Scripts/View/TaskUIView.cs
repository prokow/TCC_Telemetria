using System;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MainProject.Trindade.Core.Scripts.Quest_UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class TaskUIView : MonoBehaviour
    {
        [Header("Descrição das Tarefas")]
        [SerializeField] private TextMeshProUGUI textTask;
        [SerializeField] private Toggle toggleTask;
        
        [Header("Som da Task Completa")]
        [SerializeField] private AudioClip _taskCompleteAudio;
        public Task TaskReference {get; private set;}
        private AudioSource _audioSource;

        private void Start()
        {
            _audioSource = GetComponentInParent<AudioSource>();
            /*if (_audioSource == null)
            {
                Debug.Log(_audioSource + "not found");
            }
            else
            {
                Debug.Log(_audioSource + "found");
            }*/
        }

        // setup definido para o nome da task ser colocada no toggle
        public void Setup(Task task)
        {
            TaskReference = task;
            textTask.text = task.description;
        }

        // quando a task for completada, será indicado na UI
        public void TaskCompleted()
        {
            toggleTask.isOn = true; 
            textTask.color = Color.green;
            var toggle = toggleTask.colors;
            toggle.normalColor = Color.green;
            toggleTask.colors = toggle;
            PlayTaskCompleteAudio();
        }
        
        // aciona o som da task quando completada
        private void PlayTaskCompleteAudio()
        {
            if (_audioSource != null)
            {
                _audioSource.PlayOneShot(_taskCompleteAudio);
            }
            else
            {
                Debug.Log("audioSource is null");
            }
        }
    }
}
