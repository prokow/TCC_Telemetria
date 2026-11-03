using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.Telemetria.Supabase;
using Newtonsoft.Json;

namespace MainProjeto.Telemetria.Upload
{
    public sealed class TelemetryUploadProcessor
    {
        private readonly TelemetryUploadStore store;
        private readonly ISupabaseAccessSession auth;
        private readonly TelemetryUploadClient client;
        private readonly string projectOrigin;
        private readonly Func<DateTimeOffset> now;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public event Action<string> Status;
        public TelemetryUploadProcessor(TelemetryUploadStore store, ISupabaseAccessSession auth, TelemetryUploadClient client, string projectOrigin, Func<DateTimeOffset> now = null)
        {
            this.store = store; this.auth = auth; this.client = client;
            this.projectOrigin = new Uri(projectOrigin).GetLeftPart(UriPartial.Authority);
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }
        public async Task ProcessAsync(CancellationToken cancellationToken, bool force = false)
        {
            if (!await gate.WaitAsync(0, cancellationToken)) return;
            try
            {
                foreach (string id in store.Sessions())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TelemetryUploadState state = null;
                    try
                    {
                        state = store.Read(id);
                        if (state != null && (state.Status == "Uploaded" || state.ProjectOrigin != projectOrigin || (!force && (state.Status == "ReviewRequired" || state.NextAttemptUtc > now())))) continue;
                        string folder = store.Folder(id);
                        if (!File.Exists(Path.Combine(folder, "Session.json"))) continue;
                        // New sessions are not visible until all derived files are committed.
                        if (Directory.Exists(Path.Combine(folder, "Chunks")) && !File.Exists(Path.Combine(folder, "Ready.json"))) continue;
                        if (!File.Exists(Path.Combine(folder, "Ready.json")) && File.GetLastWriteTimeUtc(Path.Combine(folder, "Session.json")) > now().UtcDateTime.AddSeconds(-5)) continue;
                        var session = await auth.GetAsync(cancellationToken);
                        if (state != null && state.OperatorId != session.UserId) continue;
                        if (state == null)
                        {
                            var header = await Task.Run(() => TelemetryJsonStream.Read(Path.Combine(folder, "Session.json")), cancellationToken);
                            string collector = header.CollectorUserId, recordedOrigin = header.SupabaseProjectOrigin;
                            if (!string.IsNullOrEmpty(collector) && collector != session.UserId || !string.IsNullOrEmpty(recordedOrigin) && recordedOrigin != projectOrigin) continue;
                            if (TelemetryUploadPackage.ValidCode(header.ParticipantCode))
                            {
                                state = new TelemetryUploadState { SessionId = id, ParticipantCode = header.ParticipantCode, OperatorId = session.UserId, ProjectOrigin = projectOrigin };
                                await Task.Run(() => store.Save(state));
                            }
                        }
                        var package = await Task.Run(() => TelemetryUploadPackage.Build(folder, session.UserId), cancellationToken);
                        if (state == null) state = new TelemetryUploadState { SessionId = id, ParticipantCode = package.ParticipantCode, OperatorId = session.UserId, ProjectOrigin = projectOrigin, Digest = package.Digest };
                        if (state.ParticipantCode != package.ParticipantCode || (!string.IsNullOrEmpty(state.Digest) && state.Digest != package.Digest)) throw new TelemetryUploadException(TelemetryUploadFailure.Conflict, "Os arquivos locais mudaram depois de preparar o envio. Confira este UUID antes de reenviar.");
                        state.Digest = package.Digest;
                        state.Attempts++; state.Status = "Uploading"; state.ConfirmedFiles = 0; state.LastError = null; state.LastErrorType = null; await Task.Run(() => store.Save(state));
                        Status?.Invoke("Enviando sessao " + id + "...");
                        await client.UploadAsync(package, session.AccessToken, async count => { state.ConfirmedFiles = count; await Task.Run(() => store.Save(state)); }, cancellationToken);
                        state.Status = "Uploaded"; state.NextAttemptUtc = DateTimeOffset.MinValue; state.LastError = null; state.LastErrorType = null; await Task.Run(() => store.Save(state));
                        Status?.Invoke("Sessao " + id + " confirmada no Supabase (4 arquivos).");
                    }
                    catch (OperationCanceledException)
                    {
                        if (state != null && state.Status != "Uploaded") { state.Status = "Pending"; state.NextAttemptUtc = DateTimeOffset.MinValue; await Task.Run(() => store.Save(state)); }
                        throw;
                    }
                    catch (Exception error)
                    {
                        bool review = error is TelemetryUploadException uploadError && (uploadError.Failure == TelemetryUploadFailure.Conflict || uploadError.Failure == TelemetryUploadFailure.InvalidData);
                        if (error is TelemetryUploadException authError && authError.Failure == TelemetryUploadFailure.Authorization) auth.Invalidate();
                        string message = error is TelemetryUploadException || error is SupabaseAuthException || error is OperatorAuthorizationMissingException ? error.Message : "Nao foi possivel processar os arquivos locais. O registro foi preservado.";
                        if (state != null)
                        {
                            state.Status = review ? "ReviewRequired" : "Pending"; state.LastError = message;
                            state.LastErrorType = error.GetType().Name;
                            state.NextAttemptUtc = now().AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(6, state.Attempts)))); await Task.Run(() => store.Save(state));
                        }
                        Status?.Invoke("Sessao " + id + ": " + message);
                    }
                }
            }
            finally { gate.Release(); }
        }
    }
}
