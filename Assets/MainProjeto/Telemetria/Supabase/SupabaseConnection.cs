using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria.Supabase
{
    /// <summary>Configuração validada. Não contém identidade nem senha do operador.</summary>
    public sealed class SupabaseConnection
    {
        public string RestUrl { get; }
        public string PublicApiKey { get; }
        public int TimeoutSeconds { get; }

        public SupabaseConnection(string projectUrl, string publicApiKey, int timeoutSeconds = 20)
        {
            if (!Uri.TryCreate((projectUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Informe uma Project URL HTTPS válida, sem credenciais ou parâmetros.");

            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length != 0 && path != "/rest/v1")
                throw new ArgumentException("Use a Project URL ou a URL terminada em /rest/v1.");

            string key = (publicApiKey ?? string.Empty).Trim();
            if (!IsPublishableKey(key) && ReadJwtRole(key) != "anon")
                throw new ArgumentException("Use somente uma chave pública publishable ou anon. Chaves administrativas não são aceitas.");
            if (timeoutSeconds < 1 || timeoutSeconds > 120)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout deve ficar entre 1 e 120 segundos.");

            RestUrl = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/rest/v1/";
            PublicApiKey = key;
            TimeoutSeconds = timeoutSeconds;
        }

        public string GetAuthorizationHeader(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return IsPublishableKey(PublicApiKey) ? null : "Bearer " + PublicApiKey;

            string token = accessToken.Trim();
            if (ReadJwtRole(token) != "authenticated")
                throw new ArgumentException("Forneça o JWT de acesso de um usuário autenticado, sem prefixo Bearer.");
            return "Bearer " + token;
        }

        private static bool IsPublishableKey(string key)
        {
            return key.StartsWith("sb_publishable_", StringComparison.Ordinal) &&
                   key.Length > "sb_publishable_".Length &&
                   key.IndexOfAny(new[] { ' ', '\r', '\n', '\t' }) < 0;
        }

        // Inspeção defensiva do tipo da chave; assinatura e autorização são verificadas pelo Supabase.
        private static string ReadJwtRole(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token) || token.IndexOfAny(new[] { ' ', '\r', '\n', '\t' }) >= 0)
                    return null;
                string[] parts = token.Split('.');
                if (parts.Length != 3 || parts[0].Length == 0 || parts[2].Length == 0) return null;
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                var json = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                return json["role"]?.Type == JTokenType.String ? (string)json["role"] : null;
            }
            catch (Exception ex) when (ex is FormatException || ex is Newtonsoft.Json.JsonException || ex is ArgumentException)
            {
                return null;
            }
        }
    }
}
