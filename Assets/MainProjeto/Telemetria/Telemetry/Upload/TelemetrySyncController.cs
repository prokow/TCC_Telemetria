using System;
using System.IO;
using System.Threading;
using MainProjeto.Telemetria.Supabase;
using UnityEngine;

namespace MainProjeto.Telemetria.Upload
{
    [DisallowMultipleComponent]
    public sealed class TelemetrySyncController : MonoBehaviour
    {
        private static TelemetrySyncController instance;
        private static string authOrigin;
        private static SharedSupabaseSession sharedAuth;
        private TelemetryUploadProcessor processor;
        private CancellationTokenSource lifetime;
        private double nextAttempt;
        private bool running;
        public string LastStatus { get; private set; }
        public static string CurrentCollector => sharedAuth?.Current?.UserId;
        public static string CurrentProjectOrigin => authOrigin;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; authOrigin = null; sharedAuth = null; }
        public static SharedSupabaseSession GetAuthentication(SupabaseConnection connection)
        {
            string origin = new Uri(connection.RestUrl).GetLeftPart(UriPartial.Authority);
            if (sharedAuth == null || authOrigin != origin)
            {
                if (instance != null)
                {
                    instance.lifetime?.Cancel();
                    Destroy(instance.gameObject);
                    instance = null;
                }
                authOrigin = origin;
                sharedAuth = new SharedSupabaseSession(new RememberedOperatorLogin(new SupabaseAuthService(new UnitySupabaseAuthTransport(connection)), new WindowsSupabaseSessionStore(connection.RestUrl)));
            }
            return sharedAuth;
        }
        public static void StartAfterAuthentication(SupabaseConnection connection, SharedSupabaseSession authentication)
        {
            if (!Application.isPlaying) return;
            if (instance == null)
            {
                var root = new GameObject("SupabaseTelemetrySync");
                instance = root.AddComponent<TelemetrySyncController>();
                DontDestroyOnLoad(root);
            }
            if (instance.processor != null) return;
            instance.lifetime = new CancellationTokenSource();
            instance.processor = new TelemetryUploadProcessor(new TelemetryUploadStore(Path.Combine(Application.persistentDataPath, "Sessoes_Pendentes")), authentication, new TelemetryUploadClient(new UnityTelemetryUploadTransport(connection)), connection.RestUrl);
            var controller = instance;
            instance.processor.Status += message => { if (controller != null) { controller.LastStatus = message; Debug.Log("[SupabaseTelemetry] " + message); } };
            instance.nextAttempt = 0;
        }
        public static void NotifyLocalSave() { if (instance != null) instance.nextAttempt = 0; }
        private void Update()
        {
            if (processor == null || running || Time.realtimeSinceStartupAsDouble < nextAttempt) return;
            Sync(false);
        }
        [ContextMenu("Reenviar sessoes pendentes agora")]
        public void RetryPending() => Sync(true);
        private async void Sync(bool force)
        {
            if (processor == null || running || lifetime == null) return;
            running = true; nextAttempt = Time.realtimeSinceStartupAsDouble + 10;
            var token = lifetime.Token;
            try { await processor.ProcessAsync(token, force); }
            catch (OperationCanceledException) { }
            catch (Exception) { if (this != null) Debug.LogWarning("[SupabaseTelemetry] Fila indisponivel; nova tentativa sera realizada."); }
            finally { if (this != null) running = false; }
        }
        private void OnApplicationPause(bool paused) { if (!paused) nextAttempt = 0; }
        private void OnApplicationFocus(bool focused) { if (focused) nextAttempt = 0; }
        private void OnDestroy()
        {
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
            if (instance == this) instance = null;
        }
    }
}
