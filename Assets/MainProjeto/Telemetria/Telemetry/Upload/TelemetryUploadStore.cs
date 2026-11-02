using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace MainProjeto.Telemetria.Upload
{
    public sealed class TelemetryUploadState
    {
        public string ProjectOrigin, OperatorId, ParticipantCode, SessionId, Digest;
        public string Status = "Pending";
        public int Attempts, ConfirmedFiles;
        public DateTimeOffset NextAttemptUtc;
        public string LastError;
        public string LastErrorType;
    }
    public sealed class TelemetryUploadStore
    {
        public string Root { get; }
        public TelemetryUploadStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
        public string Folder(string sessionId)
        {
            if (!TelemetryUploadPackage.ValidCode(sessionId)) throw new ArgumentException("UUID invalido.");
            string path = Path.GetFullPath(Path.Combine(Root, sessionId));
            if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Pasta fora da fila.");
            return path;
        }
        public IEnumerable<string> Sessions()
            => Directory.EnumerateDirectories(Root).Select(Path.GetFileName).Where(TelemetryUploadPackage.ValidCode).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        public TelemetryUploadState Read(string sessionId)
        {
            string path = Path.Combine(Folder(sessionId), "UploadState.json");
            if (!File.Exists(path)) return null;
            var state = JsonConvert.DeserializeObject<TelemetryUploadState>(File.ReadAllText(path));
            if (state == null || state.SessionId != sessionId || !TelemetryUploadPackage.ValidCode(state.ParticipantCode) || !Guid.TryParseExact(state.OperatorId, "D", out _)) throw new InvalidDataException("Manifesto de envio invalido.");
            return state;
        }
        public void Save(TelemetryUploadState state)
        {
            Directory.CreateDirectory(Folder(state.SessionId));
            WriteAtomic(Path.Combine(Folder(state.SessionId), "UploadState.json"), JsonConvert.SerializeObject(state));
        }
        public static void WriteAtomic(string path, string content)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // ReplaceFile pode falhar ao copiar metadados/ACLs no Windows.
                // MoveFileEx troca o nome atomicamente no mesmo diretorio, sem etapa de exclusao.
                if (!MoveFileEx(temporary, path, 1 | 8)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Nao foi possivel atualizar o manifesto local.");
            }
            else if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string source, string destination, int flags);
    }
}
