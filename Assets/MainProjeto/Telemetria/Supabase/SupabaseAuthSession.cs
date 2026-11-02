using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria.Supabase
{
    /// <summary>Token somente em memória. A autorização efetiva é sempre verificada pelo servidor.</summary>
    public sealed class SupabaseAuthSession
    {
        public string AccessToken { get; }
        public string UserId { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public string RefreshToken { get; }
        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

        private SupabaseAuthSession(string token, string userId, DateTimeOffset expiresAt, string refreshToken = null)
        {
            AccessToken = token;
            UserId = userId;
            ExpiresAtUtc = expiresAt;
            RefreshToken = refreshToken;
        }

        public SupabaseAuthSession WithRefreshToken(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new SupabaseAuthException(AuthFailure.InvalidResponse, "A autorização recebida não pode ser renovada.");
            return new SupabaseAuthSession(AccessToken, UserId, ExpiresAtUtc, refreshToken);
        }

        public static SupabaseAuthSession FromAccessToken(string token, DateTimeOffset now)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token) || token.IndexOfAny(new[] { ' ', '\r', '\n', '\t' }) >= 0)
                    throw new FormatException();
                string[] parts = token.Split('.');
                if (parts.Length != 3 || parts[0].Length == 0 || parts[2].Length == 0) throw new FormatException();
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                var json = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                if (json["role"]?.Type != JTokenType.String || (string)json["role"] != "authenticated" ||
                    json["sub"]?.Type != JTokenType.String || !Guid.TryParse((string)json["sub"], out var userId) ||
                    json["exp"]?.Type != JTokenType.Integer) throw new FormatException();
                var expiry = DateTimeOffset.FromUnixTimeSeconds((long)json["exp"]);
                if (expiry <= now) throw new FormatException();
                return new SupabaseAuthSession(token, userId.ToString("D"), expiry);
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is ArgumentException || ex is OverflowException)
            {
                throw new SupabaseAuthException(AuthFailure.InvalidResponse, "A autenticação retornou uma sessão inválida. Tente entrar novamente.");
            }
        }
    }
}
