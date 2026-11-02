using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.Telemetria.Supabase;
using MainProjeto.Telemetria.Upload;
using UnityEngine;

namespace MainProjeto.MenuPrincipal.Scripts
{
    [DisallowMultipleComponent]
    public sealed class AutomaticOperatorLogin : MonoBehaviour
    {
#if UNITY_EDITOR
        // PREENCHA AQUI com a conta de Authentication que cadastra participantes.
        // Usado automaticamente ao dar Play; este bloco nao entra no build.
        private static readonly string OperatorEmail = "math.prokopowiski@gmail.com";
        private static readonly string OperatorPassword = "quimicadomal";
        private bool authorizedFromCode;
#endif

        [SerializeField] private SupabaseConnectionSettings settings;
        private SharedSupabaseSession rememberedLogin;
        private SupabaseConnection connection;
        private CancellationTokenSource lifetime;
        private SupabaseAuthSession session;
        private double nextAttempt;
        private bool busy;
        public bool IsAuthenticating => busy;
        public string ConnectionStatus { get; private set; }
        public event Action<SupabaseAuthSession> Authenticated;
        public event Action<string> StatusChanged;

        private void Awake()
        {
            try
            {
                connection = settings.CreateConnection();
                rememberedLogin = TelemetrySyncController.GetAuthentication(connection);
            }
            catch (Exception) { rememberedLogin = null; }
        }

        private void OnEnable() { lifetime = new CancellationTokenSource(); busy = false; nextAttempt = 0; }

        private void Update()
        {
            if (busy || Time.realtimeSinceStartupAsDouble < nextAttempt) return;
            if (session != null && session.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(60)) return;
            Restore();
        }

        [ContextMenu("Reconectar acesso automático (Play Mode)")]
        public async void Restore()
            => await AuthenticateAsync(RestoreConfiguredAccessAsync);

        private async Task<SupabaseAuthSession> RestoreConfiguredAccessAsync(CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            if (!authorizedFromCode && !string.IsNullOrWhiteSpace(OperatorEmail) && !string.IsNullOrEmpty(OperatorPassword))
            {
                var authorized = await rememberedLogin.AuthorizeAsync(OperatorEmail, OperatorPassword, cancellationToken);
                authorizedFromCode = true;
                return authorized;
            }
#endif
            return await rememberedLogin.GetAsync(cancellationToken);
        }

        // Usado pelo Inspector para autorizar uma vez. Email e senha não são campos serializados.
        public Task AuthorizeThisDeviceAsync(string email, string password)
            => AuthenticateAsync(token => rememberedLogin.AuthorizeAsync(email, password, token));

        private void SetStatus(string message)
        {
            ConnectionStatus = message;
            StatusChanged?.Invoke(message);
        }

        private async Task AuthenticateAsync(Func<CancellationToken, Task<SupabaseAuthSession>> authenticate)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || busy || lifetime == null) return;
            if (rememberedLogin == null)
            {
                nextAttempt = Time.realtimeSinceStartupAsDouble + 15;
                SetStatus("Configure a conexão do cadastro neste dispositivo.");
                return;
            }
            var operation = lifetime;
            var cancellationToken = operation.Token;
            busy = true;
            nextAttempt = Time.realtimeSinceStartupAsDouble + 15;
            SetStatus("Conectando ao cadastro de participantes...");
            try
            {
                var result = await authenticate(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || !ReferenceEquals(lifetime, operation)) return;
                session = result;
                TelemetrySyncController.StartAfterAuthentication(connection, rememberedLogin);
                Authenticated?.Invoke(result);
                SetStatus("Acesso autorizado neste computador. Escolha o participante na lista.");
            }
            catch (OperationCanceledException) { }
            catch (OperatorAuthorizationMissingException ex)
            {
                if (IsCurrent(operation)) SetStatus(ex.Message);
            }
            catch (PlatformNotSupportedException)
            {
                if (IsCurrent(operation))
                {
                    nextAttempt = double.PositiveInfinity;
                    SetStatus("Este dispositivo precisa de um provedor de autorização compatível.");
                }
            }
            catch (SupabaseAuthException ex)
            {
#if UNITY_EDITOR
                if (ex.Failure == AuthFailure.Credentials) authorizedFromCode = false;
#endif
                if (IsCurrent(operation))
                {
                    nextAttempt = Time.realtimeSinceStartupAsDouble + (ex.Failure == AuthFailure.Connection ? 15 : 60);
                    SetStatus(ex.Message);
                }
            }
            catch (Exception)
            {
                if (IsCurrent(operation)) SetStatus("Não foi possível autorizar o acesso. Confira a autorização deste computador.");
            }
            finally { if (ReferenceEquals(lifetime, operation)) busy = false; }
        }

        private bool IsCurrent(CancellationTokenSource operation)
            => this != null && ReferenceEquals(lifetime, operation) && !operation.IsCancellationRequested;

        private void OnDisable()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            session = null;
        }
    }
}
