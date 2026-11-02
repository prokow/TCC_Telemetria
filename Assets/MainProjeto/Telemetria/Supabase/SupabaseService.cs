using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MainProjeto.Telemetria.Supabase
{
    /// <summary>Chamar na thread principal do Unity. O transporte não conhece cena ou UI.</summary>
    public sealed class SupabaseService : ISupabaseReadClient
    {
        private readonly SupabaseConnection connection;
        private readonly Func<string> accessTokenProvider;

        public SupabaseService(SupabaseConnection connection, Func<string> accessTokenProvider = null)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.accessTokenProvider = accessTokenProvider;
        }

        public async Task<string> GetAsync(string resourceAndQuery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(resourceAndQuery) || resourceAndQuery.StartsWith("/", StringComparison.Ordinal) ||
                resourceAndQuery.Contains(":") || resourceAndQuery.Contains("..") ||
                resourceAndQuery.IndexOfAny(new[] { '\\', '\r', '\n', '#' }) >= 0)
                throw new ArgumentException("Informe um recurso REST relativo válido.");

            string authorization = connection.GetAuthorizationHeader(accessTokenProvider?.Invoke());
            using (var request = UnityWebRequest.Get(connection.RestUrl + resourceAndQuery))
            {
                request.timeout = connection.TimeoutSeconds;
                request.SetRequestHeader("apikey", connection.PublicApiKey);
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Profile", "public");
                if (authorization != null) request.SetRequestHeader("Authorization", authorization);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // Abort é executado na thread Unity, não em um callback de outro thread.
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (request.responseCode == 401 || request.responseCode == 403)
                    throw new SupabaseRequestException(SupabaseFailure.AccessDenied,
                        "Acesso negado. Confira a autenticação do responsável, os GRANTs e as políticas RLS de participants.", request.responseCode);
                if (request.result == UnityWebRequest.Result.ConnectionError)
                    throw new SupabaseRequestException(SupabaseFailure.Connection,
                        "Não foi possível conectar ao Supabase. Verifique a rede e tente novamente.", request.responseCode);
                if (request.responseCode < 200 || request.responseCode >= 300)
                    throw new SupabaseRequestException(SupabaseFailure.Http,
                        "A consulta ao Supabase falhou (HTTP " + request.responseCode + ").", request.responseCode);
                if (request.result != UnityWebRequest.Result.Success || request.downloadHandler == null)
                    throw new SupabaseRequestException(SupabaseFailure.InvalidResponse,
                        "O Supabase retornou uma resposta inválida.", request.responseCode);

                // Não registrar corpo da resposta: pode conter nomes e dados pessoais.
                return request.downloadHandler.text;
            }
        }
    }
}
