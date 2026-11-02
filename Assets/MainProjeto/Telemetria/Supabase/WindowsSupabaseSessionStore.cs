using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MainProjeto.Telemetria.Supabase
{
    public interface ISupabaseSessionStore
    {
        string LoadRefreshToken();
        void SaveRefreshToken(string token);
        void Clear();
    }

    // Manter compatível com o compilador de Add-Type do Windows PowerShell 5.
    // Não contém dependências de Unity, para o configurador usar o mesmo formato.
    public sealed class WindowsSupabaseSessionStore : ISupabaseSessionStore
    {
        private readonly string target;
        public WindowsSupabaseSessionStore(string projectUrl) { target = BuildTargetName(projectUrl); }

        public static string BuildTargetName(string projectUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                (uri.AbsolutePath.TrimEnd('/').Length != 0 && uri.AbsolutePath.TrimEnd('/') != "/rest/v1"))
                throw new ArgumentException("Project URL inválida.");
            return "CircuitosVR/Supabase/" + uri.Authority.ToLowerInvariant() + "/operator-session";
        }

        private static void RequireWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Este armazenamento de autorização é específico do Windows.");
        }

        public string LoadRefreshToken()
        {
            RequireWindows();
            IntPtr pointer;
            if (!CredRead(target, 1, 0, out pointer))
            {
                int code = Marshal.GetLastWin32Error();
                if (code == 1168) return null;
                throw new Win32Exception(code, "Não foi possível acessar a autorização local.");
            }
            byte[] bytes = null;
            try
            {
                NativeCredential credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                if (credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > 2560 || credential.CredentialBlobSize % 2 != 0)
                    throw new InvalidOperationException("A autorização local está inválida. Configure novamente.");
                bytes = new byte[(int)credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                CredFree(pointer);
            }
        }

        public void SaveRefreshToken(string token)
        {
            RequireWindows();
            if (string.IsNullOrWhiteSpace(token) || token.Length > 1280)
                throw new ArgumentException("Token de renovação inválido.");
            byte[] bytes = Encoding.Unicode.GetBytes(token);
            IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                NativeCredential credential = new NativeCredential();
                credential.Type = 1;
                credential.TargetName = target;
                credential.Comment = "Autorizacao do simulador Circuitos VR";
                credential.CredentialBlobSize = (uint)bytes.Length;
                credential.CredentialBlob = blob;
                credential.Persist = 2; // Perfil Windows atual, persistente neste computador.
                credential.UserName = "CircuitosVR";
                if (!CredWrite(ref credential, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível salvar a autorização local.");
            }
            finally
            {
                for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
                Marshal.FreeCoTaskMem(blob);
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public void Clear()
        {
            RequireWindows();
            if (!CredDelete(target, 1, 0))
            {
                int code = Marshal.GetLastWin32Error();
                if (code != 1168) throw new Win32Exception(code, "Não foi possível remover a autorização local.");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
    }
}
