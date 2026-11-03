using System;
using System.IO;
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
        public Task<TelemetryHttpResponse> SendAsync(string method, string path, string accessToken, byte[] body, string contentType, CancellationToken token)
            => Send(method, path, accessToken, body, null, null, contentType, token);
        public Task<TelemetryHttpResponse> SendFileAsync(string path, string accessToken, string filePath, string contentType, CancellationToken token)
            => Send("POST", path, accessToken, null, filePath, null, contentType, token);
        public async Task<TelemetryHttpResponse> DownloadHashAsync(string path, string accessToken, string temporaryPath, CancellationToken token)
        {
            try
            {
                var result = await Send("GET", path, accessToken, null, null, temporaryPath, null, token);
                if (result.Status >= 200 && result.Status < 300)
                    result.Sha256 = await Task.Run(() => TelemetryUploadPackage.HashFile(temporaryPath), token);
                return result;
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }
        private async Task<TelemetryHttpResponse> Send(string method, string path, string accessToken, byte[] body,
            string uploadPath, string downloadPath, string contentType, CancellationToken cancellationToken)
        {
            if ((!path.StartsWith("/storage/v1/object/", StringComparison.Ordinal) && !path.StartsWith("/rest/v1/rpc/", StringComparison.Ordinal)) || path.Contains("..") || path.Contains(":") || path.Contains("\\")) throw new ArgumentException("Recurso de envio invalido.");
            cancellationToken.ThrowIfCancellationRequested();
            string origin = new Uri(connection.RestUrl).GetLeftPart(UriPartial.Authority);
            using (var request = new UnityWebRequest(origin + path, method))
            {
                request.timeout = Math.Min(120, Math.Max(60, connection.TimeoutSeconds * 3));
                request.redirectLimit = 0;
                if (downloadPath == null) request.downloadHandler = new DownloadHandlerBuffer();
                else request.downloadHandler = new DownloadHandlerFile(downloadPath) { removeFileOnAbort = true };
                if (uploadPath != null) request.uploadHandler = new UploadHandlerFile(uploadPath);
                else if (body != null) request.uploadHandler = new UploadHandlerRaw(body);
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
                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
                    throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Falha na transferencia. Arquivos mantidos para reenvio.");
                return new TelemetryHttpResponse(request.responseCode, downloadPath == null ? request.downloadHandler.data : null);
            }
        }
    }
}