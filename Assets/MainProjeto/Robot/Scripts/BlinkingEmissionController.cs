using System.Collections;
using UnityEngine;

namespace MainProjeto.Robot.Scripts
{
    public class BlinkingEmissionController : MonoBehaviour
    {
        [Header("Material do Olho do Robo")]
        [SerializeField] 
        private Material materialRobot;

        [Header("Configurações de Tempo")]
        [SerializeField] private float tempoLigado = 0.5f;
        [SerializeField] private float tempoDesligado = 0.5f;
        
        [Header("Comportamento")]
        [Tooltip("Marque se quiser que ele já comece piscando assim que o objeto for ativado na cena.")]
        [SerializeField] private bool piscarAoIniciar = false;

        private Material _material;
        private Coroutine _rotinaPiscar;

        private void Awake()
        {
            if (materialRobot == null) return;

            if (materialRobot != null)
            {
                // Pega a instância local do material para não afetar outros objetos iguais na cena
                _material = materialRobot;
            }
        }

        private void OnEnable()
        {
            if (piscarAoIniciar) StartBlinking();
            
            // Se inscreve para ouvir o robô
            RobotFeedbackManager.OnMaterialFeedback += IniciarPiscarPorTempo; 
        }

        private void OnDisable()
        {
            StopBlinking();
            
            // Remove a inscrição por segurança
            RobotFeedbackManager.OnMaterialFeedback-= IniciarPiscarPorTempo;
        }

        // Método público para iniciar o piscar (pode ser chamado via UnityEvent)
        public void StartBlinking()
        {
            if (_material == null) return;

            if (_rotinaPiscar == null)
            {
                _rotinaPiscar = StartCoroutine(RotinaPiscar());
            }
        }
        
        private void IniciarPiscarPorTempo(float duracaoDoAudio)
        {
            StartBlinking();
            
            // Inicia uma contagem para parar de piscar quando o áudio acabar
            StartCoroutine(PararBlinkingAposTempo(duracaoDoAudio));
        }

        private IEnumerator PararBlinkingAposTempo(float tempo)
        {
            yield return new WaitForSeconds(tempo);
            StopBlinking();
        }

        // Método público para parar o piscar
        public void StopBlinking()
        {
            if (_rotinaPiscar != null)
            {
                StopCoroutine(_rotinaPiscar);
                _rotinaPiscar = null;
            }
            
            // Garante que a emissão termine desligada por segurança
            if (_material != null)
            {
                _material.DisableKeyword("_EMISSION");
            }
        }

        private IEnumerator RotinaPiscar()
        {
            while (true)
            {
                _material.EnableKeyword("_EMISSION");
                yield return new WaitForSeconds(tempoLigado);
                
                _material.DisableKeyword("_EMISSION");
                yield return new WaitForSeconds(tempoDesligado);
            }
        }
    }
}