using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MainProjeto.Telemetria.Supabase
{
    public enum AuthFailure { Input, Credentials, EmailNotConfirmed, RateLimit, Connection, Configuration, InvalidResponse }

    public sealed class SupabaseAuthException : Exception
    {
        public AuthFailure Failure { get; }
        public SupabaseAuthException(AuthFailure failure, string message) : base(message) => Failure = failure;
    }

    public sealed class AuthHttpResponse
    {
        public long StatusCode { get; }
        public string Json { get; }
        public AuthHttpResponse(long statusCode, string json) { StatusCode = statusCode; Json = json; }
    }

    public interface ISupabaseAuthTransport
    {
        Task<AuthHttpResponse> SignInAsync(string jsonBody, CancellationToken cancellationToken);
    }

    public interface ISupabaseSessionTransport : ISupabaseAuthTransport
    {
        Task<AuthHttpResponse> RefreshAsync(string jsonBody, CancellationToken cancellationToken);
    }

    public sealed class SupabaseAuthService
    {
        private readonly ISupabaseAuthTransport transport;
        public SupabaseAuthService(ISupabaseAuthTransport transport)
            => this.transport = transport ?? throw new ArgumentNullException(nameof(transport));

        public async Task<SupabaseAuthSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            email = (email ?? string.Empty).Trim();
            if (email.IndexOf('@') <= 0 || email.EndsWith("@", StringComparison.Ordinal) || string.IsNullOrEmpty(password))
                throw new SupabaseAuthException(AuthFailure.Input, "Preencha seu e-mail e sua senha.");

            string json = new JObject { ["email"] = email, ["password"] = password }.ToString(Formatting.None);
            // Não manter a senha em campos, logs, PlayerPrefs ou assets.
            password = null;
            AuthHttpResponse response = await transport.SignInAsync(json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ParseResponse(response, false);
        }

        public async Task<SupabaseAuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new SupabaseAuthException(AuthFailure.Input, "Autorize este dispositivo antes de usar o acesso automático.");
            if (!(transport is ISupabaseSessionTransport sessionTransport))
                throw new SupabaseAuthException(AuthFailure.Configuration, "O serviço não oferece renovação de acesso.");
            string json = new JObject { ["refresh_token"] = refreshToken }.ToString(Formatting.None);
            var response = await sessionTransport.RefreshAsync(json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ParseResponse(response, true);
        }

        private static SupabaseAuthSession ParseResponse(AuthHttpResponse response, bool refreshing)
        {
            if (response == null) throw InvalidResponse();
            if (response.StatusCode == 429)
                throw new SupabaseAuthException(AuthFailure.RateLimit, "Muitas tentativas. Aguarde um pouco antes de tentar novamente.");
            JObject body;
            try { body = JObject.Parse(response.Json ?? string.Empty); }
            catch (JsonException) { throw InvalidResponse(); }

            if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                if (body["access_token"]?.Type != JTokenType.String) throw InvalidResponse();
                var session = SupabaseAuthSession.FromAccessToken((string)body["access_token"], DateTimeOffset.UtcNow);
                if (body["refresh_token"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)body["refresh_token"]))
                    return session.WithRefreshToken((string)body["refresh_token"]);
                if (refreshing) throw InvalidResponse();
                return session;
            }

            string code = body["error_code"]?.Type == JTokenType.String ? (string)body["error_code"] : null;
            if (code == "invalid_credentials")
                throw new SupabaseAuthException(AuthFailure.Credentials, "E-mail ou senha incorretos.");
            if (code == "email_not_confirmed")
                throw new SupabaseAuthException(AuthFailure.EmailNotConfirmed, "Confirme seu e-mail antes de entrar.");
            if (refreshing && (response.StatusCode == 400 || response.StatusCode == 401 || response.StatusCode == 403))
                throw new SupabaseAuthException(AuthFailure.Credentials, "A autorização deste dispositivo expirou ou foi revogada. Configure o acesso novamente.");
            throw new SupabaseAuthException(AuthFailure.Configuration, "Não foi possível entrar. Verifique a conta e a configuração da aplicação.");
        }

        private static SupabaseAuthException InvalidResponse() => new SupabaseAuthException(
            AuthFailure.InvalidResponse, "Não foi possível validar a resposta do login. Tente novamente.");
    }

    /// <summary>Usar na thread principal do Unity; envio HTTPS somente para Supabase Auth.</summary>
    public sealed class UnitySupabaseAuthTransport : ISupabaseSessionTransport
    {
        private readonly SupabaseConnection connection;
        public UnitySupabaseAuthTransport(SupabaseConnection connection)
            => this.connection = connection ?? throw new ArgumentNullException(nameof(connection));

        public Task<AuthHttpResponse> SignInAsync(string jsonBody, CancellationToken cancellationToken)
            => PostAsync("password", jsonBody, cancellationToken);

        public Task<AuthHttpResponse> RefreshAsync(string jsonBody, CancellationToken cancellationToken)
            => PostAsync("refresh_token", jsonBody, cancellationToken);

        private async Task<AuthHttpResponse> PostAsync(string grantType, string jsonBody, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = new Uri(connection.RestUrl).GetLeftPart(UriPartial.Authority) + "/auth/v1/token?grant_type=" + grantType;
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = connection.TimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", connection.PublicApiKey);
                string authorization = connection.GetAuthorizationHeader(null);
                if (authorization != null) request.SetRequestHeader("Authorization", authorization);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (request.result == UnityWebRequest.Result.ConnectionError)
                    throw new SupabaseAuthException(AuthFailure.Connection, "Sem conexão. Verifique a rede e tente novamente.");
                if (request.result == UnityWebRequest.Result.DataProcessingError || request.downloadHandler == null)
                    throw new SupabaseAuthException(AuthFailure.InvalidResponse, "Não foi possível receber a resposta do login.");
                return new AuthHttpResponse(request.responseCode, request.downloadHandler.text);
            }
        }
    }
}
