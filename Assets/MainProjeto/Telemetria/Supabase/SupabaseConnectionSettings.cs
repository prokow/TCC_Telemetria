using UnityEngine;

namespace MainProjeto.Telemetria.Supabase
{
    [CreateAssetMenu(menuName = "MainProjeto/Telemetria/Conexão Supabase", fileName = "SupabaseConnectionSettings")]
    public sealed class SupabaseConnectionSettings : ScriptableObject
    {
        [SerializeField, Tooltip("Project URL HTTPS; /rest/v1 no final também é aceito.")]
        private string projectUrl;
        [SerializeField, Tooltip("Somente anon ou publishable. Nunca service_role ou secret.")]
        private string publicApiKey;
        [SerializeField, Range(1, 120)] private int timeoutSeconds = 20;

        public SupabaseConnection CreateConnection()
        {
            return new SupabaseConnection(projectUrl, publicApiKey, timeoutSeconds);
        }
    }
}
