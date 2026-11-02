using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SupabaseManager : MonoBehaviour
{
    [Header("Credenciais do Supabase")]
    [Tooltip("Sua Project URL do Supabase")]
    public string supabaseUrl = "https://qvszuzmwyfuyydfjjvxb.supabase.co";
        
    [Tooltip("Sua Anon Public Key do Supabase")]
    public string supabaseAnonKey = "sb_publishable_BZa9jareY-i09g34iMS78w_W01dlinH";

    // Nome exato da função RPC que criamos no SQL
    private string rpcEndpoint = "/rest/v1/rpc/salvar_telemetria_completa";

    public void SendTelemetryData(string jsonPayload)
    {
        StartCoroutine(PostRequestRoutine(jsonPayload));
    }

    private IEnumerator PostRequestRoutine(string json)
    {
        string url = supabaseUrl + rpcEndpoint;

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
                
            // Cabeçalhos obrigatórios do Supabase e REST
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", supabaseAnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseAnonKey);

            Debug.Log("Enviando telemetria para o banco de dados...");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Erro ao enviar para o Supabase: {request.error}\nDetalhes: {request.downloadHandler.text}");
            }
            else
            {
                Debug.Log("<color=green>[Supabase] Telemetria salva no banco de dados com sucesso!</color>");
            }
        }
    }
}
