using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainProjeto.Telemetria
{
    // Serviço sem MonoBehaviour. Acesso a disco apenas ao encerrar (curtas sessões).
    public static class TelemetryLocalStorage
    {
        public static string GetSessionFolder(string sessionId)
        {
            if (!Upload.TelemetryUploadPackage.ValidCode(sessionId)) throw new ArgumentException("UUID de sessao invalido.");
            return Path.Combine(Application.persistentDataPath, "Sessoes_Pendentes", sessionId);
        }

        public static string Save(TelemetrySessionPayload payload)
        {
            if (payload == null || !Upload.TelemetryUploadPackage.ValidCode(payload.ParticipantCode))
                throw new ArgumentException("Payload ou SessionId inválidos.");

            string folder = GetSessionFolder(payload.SessionId);
            Directory.CreateDirectory(folder);

            // JSON é o registro canônico e é gravado antes dos CSVs derivados.
            string jsonPath = Path.Combine(folder, "Session.json");
            string tempPath = jsonPath + ".tmp";
            string json = JsonUtility.ToJson(payload, false);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (!File.Exists(jsonPath)) File.Move(tempPath, jsonPath);
            else File.Delete(tempPath);  // Reenvios não reescrevem o JSON finalizado.

            foreach (var pair in TelemetryCsvEncoder.Encode(payload)) Upload.TelemetryUploadStore.WriteAtomic(Path.Combine(folder, pair.Key), pair.Value);
            Upload.TelemetryUploadStore.WriteAtomic(Path.Combine(folder, "Ready.json"), "{\"ready\":true}");
            Debug.Log("[TelemetryLocalStorage] Sessão salva em: " + folder);
            return jsonPath;
        }

    }
}
