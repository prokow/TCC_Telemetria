using UnityEngine;
using UnityEngine.SceneManagement;

public class EndingGame : MonoBehaviour
{
    [Header("GameObject para voltar ao menu")]
    [SerializeField]
    private GameObject endingGameUI;
    
    private void Awake()
    {
        if (endingGameUI != null)
        {
            endingGameUI.SetActive(false);
        }
    }

    public void ShowEndMenu()
    {
        if (endingGameUI != null)
        { 
            endingGameUI.SetActive(true);
        }
    }

    public void BackToMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
    
}
