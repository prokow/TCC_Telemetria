using System.Collections;
using TMPro;
using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    public class BallonText : MonoBehaviour
    {
        private TextMeshProUGUI textComponent;
        private Camera _cam;

        [Header("Tempo das Letras aparecerem no Balão")] [SerializeField]
        private float tempoEntreLetras = 0.05f;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Awake()
        {
            _cam = Camera.main;
            textComponent = GetComponentInChildren<TextMeshProUGUI>();
        }

        public void Setup(string text, float lifeTime)
        {
            StartCoroutine(TypeWrittingCoroutine(text, lifeTime));
        }

        private IEnumerator TypeWrittingCoroutine(string textoCompleto, float tempoEspera)
        {
            textComponent.text = "";

            foreach (char letra in textoCompleto.ToCharArray())
            {
                textComponent.text += letra;

                yield return new WaitForSeconds(tempoEntreLetras);
            }

            Destroy(gameObject, tempoEspera);
        }

// Update is called once per frame
        void LateUpdate()
        {
            if (_cam != null)
            {
                transform.forward = -_cam.transform.forward;
            }
        }
    }
}