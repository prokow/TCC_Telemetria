using System.Collections;
using MainProject.Trindade.Core.Scripts.Quest_UI;
using MainProject.Trindade.Core.Scripts.ScriptableObjects;
using TMPro;
using UnityEngine;

namespace MainProjeto.Core.Scripts.View
{
    [RequireComponent(typeof(AudioSource))]
    public class QuestUIView : MonoBehaviour
    {
        [Header("Titulo da Interação")]
        [SerializeField] 
        private TextMeshProUGUI textMainTitle;
        
        [Header("Texto de Interação Concluida")]
        [SerializeField] 
        private TextMeshProUGUI textCompleted;
        
        // Painéis a serem ativados e desativados
        [Header("GameObjects UI")]
        [SerializeField] 
        private GameObject taskPanel;
        [SerializeField] 
        private GameObject endPanel;

        [Header("Prefab's a serem spawnado como tarefas")]
        [SerializeField] 
        private GameObject prefabTask;
        [SerializeField] 
        private Transform prefabContainer;
    
        [Header("Efeito Sonoros da Task e Quest")]
        [SerializeField] 
        private AudioClip taskCompleteSound;
        [SerializeField] 
        private AudioClip questCompleteSound;
        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }
        
        public void SetTitle(string title)
        {
            if (textMainTitle != null)
            {
                textMainTitle.SetText(title);
            }
            else
            {
                Debug.LogWarning("textMainTitle not found");
            }
        }

        // painéis a serem ativos quando a quest for ativada
        public void TogglePanels(bool isQuestActive)
        {
            if (taskPanel != null && endPanel != null)
            {
                taskPanel.SetActive(isQuestActive);
                endPanel.SetActive(!isQuestActive);
            }
            else
            {
                Debug.LogWarning("panel's not found");
            }
        }

        public void ShowQuestCompleteMessage()
        {
            textCompleted.SetText("Aguarde próximas orientações!");
            PlayQuestCompleteSound();
        }

        private void PlayQuestCompleteSound()
        {
            if (_audioSource != null && questCompleteSound != null)
            {
                _audioSource.PlayOneShot(questCompleteSound);
            }
            else
            {
                Debug.LogWarning("audioSource and QuestCompleteSound not found!");
            }
        }

        // remove as tarefas feitas quando completado a quest por completo
        public void ClearContainer()
        {
            foreach (Transform child in prefabContainer)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        // adiciona a task na UI
        public TaskUIView CreateTaskUI(Task taskData)
        {
            GameObject prefab = Instantiate(prefabTask, prefabContainer);
            TaskUIView item = prefab.GetComponent<TaskUIView>();
            item.Setup(taskData);

            CanvasGroup canvasGroup = item.GetComponent<CanvasGroup>();
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            prefab.SetActive(false);

            return item;
        }
        
        // fade para ir de uma task para outra
        public IEnumerator FadeRoutine(CanvasGroup canvasGroup, float start, float end, float duration)
        {
            if (canvasGroup == null) yield break;
            
            float elapsedTime = 0f;
            canvasGroup.alpha = start;

            while (elapsedTime < duration)
            {
                if (canvasGroup == null) yield break;
                elapsedTime += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, end, elapsedTime / duration);
                yield return null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = end;
            }
        }
    }
}