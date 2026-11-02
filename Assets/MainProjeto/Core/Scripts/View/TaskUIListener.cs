using System.Collections;
using MainProjeto.Core.Scripts.Model;
using UnityEngine;

namespace MainProject.Trindade.Core.Scripts.Quest_UI
{
    public class TaskUIListener : MonoBehaviour
    {
        [Header("Transform's position of Task UI")]
        [SerializeField] 
        private Transform[] transformPoint;

        [Header("UI for Quest Finished")]
        [SerializeField] 
        private GameObject[] finishedUI;
        
        private int _index = 0;
    
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            if (QuestSystemModel.Instance != null)
            {
                QuestSystemModel.Instance.onQuestComplete.AddListener(VerifiedQuestCompleted);
            }

            foreach (var canvas in finishedUI)
            {
                canvas.SetActive(false);
            }
        }

        private void TeleportUI()
        {
            if (_index < transformPoint.Length && transformPoint[_index] != null)
            {
                Transform nextPoint = transformPoint[_index];
                
                transform.position = nextPoint.position;
                transform.rotation = nextPoint.rotation;
                transform.localScale = nextPoint.localScale;

                if (_index < finishedUI.Length && finishedUI[_index] != null)
                {
                    finishedUI[_index].SetActive(true);
                }
                
                _index++;    // próximo transform
                
            }
        }
    
        private void VerifiedQuestCompleted()
        {
            if (QuestSystemModel.Instance != null || transformPoint != null)
            {
                StartCoroutine(DelayedUpdate(7.0f));
            }
        }


        private IEnumerator DelayedUpdate(float delay)
        {
            yield return new WaitForSeconds(delay);
            TeleportUI();
        }
    }
}
