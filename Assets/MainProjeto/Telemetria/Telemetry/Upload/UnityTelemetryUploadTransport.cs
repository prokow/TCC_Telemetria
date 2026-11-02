using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.Telemetria.Supabase;
using UnityEngine.Networking;

namespace MainProjeto.Telemetria.Upload
{
    public sealed class UnityTelemetryUploadTransport : ITelemetryUploadTransport
    {
        private readonly SupabaseConnection connection;
        public UnityTelemetryUploadTransport(SupabaseConnection connection) => this.connection = connection;
        public async Task<TelemetryHttpResponse> SendAsync(string method, string path, string accessToken, byte[] body, string contentType, CancellationToken cancellationToken)
        {
            if ((!path.StartsWith("/storage/v1/object/", StringComparison.Ordinal) && !path.StartsWith("/rest/v1/rpc/", StringComparison.Ordinal)) || path.Contains("..") || path.Contains(":") || path.Contains("\\")) throw new ArgumentException("Recurso de envio invalido.");
            cancellationToken.ThrowIfCancellationRequested();
            string origin = new Uri(connection.RestUrl).GetLeftPart(UriPartial.Authority);
            using (var request = new UnityWebRequest(origin + path, method))
            {
                request.timeout = Math.Min(120, Math.Max(60, connection.TimeoutSeconds * 3));
                request.redirectLimit = 0;
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null) request.uploadHandler = new UploadHandlerRaw(body);
                request.SetRequestHeader("apikey", connection.PublicApiKey);
                request.SetRequestHeader("Authorization", connection.GetAuthorizationHeader(accessToken));
                if (contentType != null) request.SetRequestHeader("Content-Type", contentType);
                if (method == "POST" && path.StartsWith("/storage/", StringComparison.Ordinal)) request.SetRequestHeader("x-upsert", "false");
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested) { request.Abort(); cancellationToken.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (request.result == UnityWebRequest.Result.ConnectionError) throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Sem conexao com o Supabase. Arquivos mantidos para reenvio.");
                if (request.result == UnityWebRequest.Result.DataProcessingError) throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Resposta de transferencia invalida. Nova tentativa agendada.");
                return new TelemetryHttpResponse(request.responseCode, request.downloadHandler.data);
            }
        }
    }
}
