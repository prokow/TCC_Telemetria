using MainProjeto.Telemetria;
using TMPro;
using UnityEngine;

namespace MainProjeto.UI.Scripts
{
    // Mantém os contadores exibidos no painel e informa as tentativas ao gerenciador de telemetria.
    // Compatível com scripts existentes que usam RegisterSucess() ou RegisterError().
    public class SessionMetricsManager : MonoBehaviour
    {
        public static SessionMetricsManager Instance { get; private set; }

        [Header("Feedback no TextMeshPro")]
        [SerializeField] private TextMeshProUGUI tentativasText;
        [SerializeField] private TextMeshProUGUI acertosText;
        [SerializeField] private TextMeshProUGUI errorsText;

        [Header("Telemetria")]
        [SerializeField] private TelemetrySessionManager telemetrySessionManager;
        [SerializeField] private string currentTaskId = "divisor_tensao";

        private int _acertos;
        private int _erros;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (tentativasText == null || acertosText == null || errorsText == null)
                Debug.LogWarning("[SessionMetricsManager] Referências de texto incompletas.");
            if (telemetrySessionManager == null)
                Debug.LogWarning("[SessionMetricsManager] TelemetrySessionManager não atribuído; somente UI será atualizada.");
            UpdateUI();
        }

        public void SetCurrentTaskId(string taskId)
        {
            if (!string.IsNullOrWhiteSpace(taskId)) currentTaskId = taskId.Trim();
        }

        // Nome original preservado para não quebrar seus scripts existentes.
        public void RegisterSucess() => RegisterResult(true);
        public void RegisterSuccess() => RegisterResult(true);
        public void RegisterError() => RegisterResult(false);

        private void RegisterResult(bool success)
        {
            if (success) _acertos++;
            else _erros++;

            // Uma tentativa é registrada UMA vez, aqui. Não chame RegisterAttempt
            // novamente nos scripts da protoboard para a mesma tentativa.
            if (telemetrySessionManager != null)
                telemetrySessionManager.RegisterAttempt(success, currentTaskId);

            UpdateUI();
        }

        // O painel pode zerar entre fases; os totais da sessão continuam
        // acumulados dentro de TelemetrySessionManager.
        public void ResetMetrics()
        {
            _acertos = 0;
            _erros = 0;
            UpdateUI();
            Debug.Log("[SessionMetricsManager] Métricas visíveis zeradas para a nova fase.");
        }

        private void UpdateUI()
        {
            if (acertosText != null) acertosText.text = $"Acertos: {_acertos}";
            if (errorsText != null) errorsText.text = $"Erros: {_erros}";
            if (tentativasText != null) tentativasText.text = $"Tentativas: {_acertos + _erros}";
        }

        public int GetAcertos() => _acertos;
        public int GetErros() => _erros;
        public int GetTentativas() => _acertos + _erros;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
