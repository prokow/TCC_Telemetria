using UnityEngine;

namespace MainProjeto.Telemetria.Supabase
{
    public static class SupabaseSessionStore
    {
        public static ISupabaseSessionStore Create(string projectUrl)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSupabaseSessionStore(projectUrl);
#else
            return new WindowsSupabaseSessionStore(projectUrl);
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    public sealed class AndroidSupabaseSessionStore : ISupabaseSessionStore
    {
        private readonly string target;
        public AndroidSupabaseSessionStore(string projectUrl)
        {
            target = WindowsSupabaseSessionStore.BuildTargetName(projectUrl);
        }

        public string LoadRefreshToken()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var store = new AndroidJavaClass("com.circuitosvr.auth.OperatorSessionStore");
            return store.CallStatic<string>("load", activity, target);
        }

        public void SaveRefreshToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new System.ArgumentException("Token de renovação inválido.");
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var store = new AndroidJavaClass("com.circuitosvr.auth.OperatorSessionStore");
            store.CallStatic("save", activity, target, token);
        }

        public void Clear()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var store = new AndroidJavaClass("com.circuitosvr.auth.OperatorSessionStore");
            store.CallStatic("clear", activity, target);
        }
    }
#endif
}
