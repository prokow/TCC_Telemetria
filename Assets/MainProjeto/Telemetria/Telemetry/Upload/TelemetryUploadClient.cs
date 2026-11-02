using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria.Upload
{
    public sealed class TelemetryHttpResponse
    {
        public long Status;
        public byte[] Bytes;
        public TelemetryHttpResponse(long status, byte[] bytes) { Status = status; Bytes = bytes ?? Array.Empty<byte>(); }
        public static TelemetryHttpResponse Json(long status, string text) => new TelemetryHttpResponse(status, Encoding.UTF8.GetBytes(text));
    }
    public interface ITelemetryUploadTransport
    {
        Task<TelemetryHttpResponse> SendAsync(string method, string path, string accessToken, byte[] body, string contentType, CancellationToken cancellationToken);
    }
    public sealed class TelemetryUploadClient
    {
        private readonly ITelemetryUploadTransport transport;
        public TelemetryUploadClient(ITelemetryUploadTransport transport) => this.transport = transport;
        public async Task UploadAsync(TelemetryUploadPackage package, string accessToken, Action<int> progress, CancellationToken cancellationToken)
        {
            var checkBody = Encoding.UTF8.GetBytes(new JObject { ["p_participant_code"] = package.ParticipantCode }.ToString(Formatting.None));
            var check = await transport.SendAsync("POST", "/rest/v1/rpc/participant_telemetry_access", accessToken, checkBody, "application/json", cancellationToken);
            EnsureSuccess(check);
            if (Encoding.UTF8.GetString(check.Bytes).Trim() != "true") throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "UUID do participante nao cadastrado ou sem acesso. O arquivo local foi preservado.");
            int confirmed = 0;
            foreach (var file in package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = "/storage/v1/object/vr-telemetry/" + string.Join("/", file.StoragePath.Split('/').Select(Uri.EscapeDataString));
                var result = await transport.SendAsync("POST", path, accessToken, file.Bytes, file.ContentType, cancellationToken);
                if (result == null) EnsureSuccess(null);
                bool exists = false;
                if (result.Status == 400 || result.Status == 409)
                {
                    try { var error = JObject.Parse(Encoding.UTF8.GetString(result.Bytes)); exists = (string)error["error"] == "Duplicate" || (string)error["code"] == "Duplicate" || ((string)error["message"] ?? "").IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0; }
                    catch (JsonException) { }
                }
                if (exists)
                {
                    var existing = await transport.SendAsync("GET", path.Replace("/object/vr-telemetry/", "/object/authenticated/vr-telemetry/"), accessToken, null, null, cancellationToken);
                    EnsureSuccess(existing);
                    if (TelemetryUploadPackage.Hash(existing.Bytes) != file.Sha256) throw new TelemetryUploadException(TelemetryUploadFailure.Conflict, "Ja existe outro conteudo para este UUID no Storage. Nenhum arquivo foi sobrescrito.");
                }
                else EnsureSuccess(result);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(++confirmed);
            }
            var response = await transport.SendAsync("POST", "/rest/v1/rpc/ingest_telemetry_session", accessToken, Encoding.UTF8.GetBytes(package.RequestBody.ToString(Formatting.None)), "application/json", cancellationToken);
            EnsureSuccess(response);
            cancellationToken.ThrowIfCancellationRequested();
            JObject ack;
            try { ack = JObject.Parse(Encoding.UTF8.GetString(response.Bytes)); }
            catch (JsonException) { throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Resposta de confirmacao invalida. O envio sera conferido novamente."); }
            if ((string)ack["session_id"] != package.SessionId || (string)ack["participant_code"] != package.ParticipantCode || (string)ack["digest"] != package.Digest || (int?)ack["file_count"] != 4 || (int?)ack["event_count"] != package.Events.Count || !new[] { "uploaded", "already_uploaded" }.Contains((string)ack["status"]))
                throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Confirmacao nao corresponde aos UUIDs e arquivos enviados.");
        }
        private static void EnsureSuccess(TelemetryHttpResponse response)
        {
            if (response == null) throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Sem resposta do Supabase.");
            if (response.Status >= 200 && response.Status < 300) return;
            string code = "";
            try { var error = JObject.Parse(Encoding.UTF8.GetString(response.Bytes)); code = (string)error["code"] ?? (string)error["error"]; } catch (JsonException) { }
            if (response.Status == 401) throw new TelemetryUploadException(TelemetryUploadFailure.Authorization, "Acesso expirado. Renovando autorizacao para reenviar.");
            if (response.Status == 403 || response.Status == 404 || code == "42501" || code == "Unauthorized" || code == "AccessDenied") throw new TelemetryUploadException(TelemetryUploadFailure.Configuration, "Envio nao autorizado ou API ausente. Execute a migracao 007 no Supabase.");
            if (code == "23505") throw new TelemetryUploadException(TelemetryUploadFailure.Conflict, "UUID de sessao ja associado a outro conteudo no banco.");
            if (response.Status == 429 || response.Status >= 500) throw new TelemetryUploadException(TelemetryUploadFailure.Connection, "Supabase temporariamente indisponivel. Reenvio agendado.");
            if (response.Status == 413) throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "O projeto Supabase recusou o tamanho do arquivo. Confira os limites do Storage ou particione a coleta; o backup local foi preservado.");
            throw new TelemetryUploadException(TelemetryUploadFailure.InvalidData, "Supabase recusou o formato dos dados (HTTP " + response.Status + "). Confira o manifesto e a migracao 007.");
        }
    }
}
