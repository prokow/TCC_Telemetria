using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace MainProjeto.Telemetria
{
    public static class TelemetryLocalStorage
    {
        // Capture Unity's path on the main thread before starting workers.
        public static string Root => Path.Combine(Application.persistentDataPath, "Sessoes_Pendentes");
        public static string GetSessionFolder(string sessionId)
        {
            if (!Upload.TelemetryUploadPackage.ValidCode(sessionId)) throw new ArgumentException("UUID de sessao invalido.");
            return Path.Combine(Root, sessionId);
        }
        public static Task<System.Collections.Generic.List<string>> RecoverAsync(string root)
            => Task.Run(() => TelemetrySessionWriter.Recover(root));
    }
}