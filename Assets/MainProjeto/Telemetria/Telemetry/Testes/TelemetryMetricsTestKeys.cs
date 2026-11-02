using MainProjeto.UI.Scripts;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace MainProjeto.Telemetria
{
    /// <summary>
    /// Script TEMPORÁRIO para validar a Etapa 03 sem depender dos circuitos.
    /// Durante Play no Editor: T inicia/encerra a sessão (TelemetrySessionInput),
    /// tecla 1 simula acerto e tecla 2 simula erro.
    /// Remova/desabilite o componente depois de concluir os testes.
    /// </summary>
    public class TelemetryMetricsTestKeys : MonoBehaviour
    {
        [SerializeField] private TelemetrySessionManager sessionManager;
        [SerializeField] private SessionMetricsManager metricsManager;

        private void Update()
        {
#if UNITY_EDITOR
            if (sessionManager == null || metricsManager == null ||
                !sessionManager.IsRecording || Keyboard.current == null)
                return;

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                metricsManager.RegisterSucess();
                Debug.Log("[TelemetryMetricsTestKeys] Acerto de teste registrado.");
            }

            if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                metricsManager.RegisterError();
                Debug.Log("[TelemetryMetricsTestKeys] Erro de teste registrado.");
            }
#endif
        }
    }
}
