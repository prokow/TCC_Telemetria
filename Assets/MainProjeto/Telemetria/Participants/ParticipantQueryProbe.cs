using System;
using System.Threading;
using MainProjeto.Telemetria.Supabase;
using UnityEngine;

namespace MainProjeto.Telemetria.Participants
{
    /// <summary>Verificação manual da etapa 4; não altera UI, seleção ou sessão.</summary>
    public sealed class ParticipantQueryProbe : MonoBehaviour
    {
        [SerializeField] private SupabaseConnectionSettings settings;
        private CancellationTokenSource queryCancellation;
        private string accessToken;

        // O futuro fluxo de autenticação fornece o JWT em memória. Nunca serializar no Inspector.
        public void SetAccessToken(string token) => accessToken = token;

        [ContextMenu("Consultar participantes (Play Mode)")]
        public async void QueryParticipants()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                Debug.LogWarning("[Participantes] Execute a consulta em Play Mode, com o componente ativo.");
                return;
            }
            if (queryCancellation != null) return;
            if (settings == null)
            {
                Debug.LogWarning("[Participantes] Atribua o asset de conexão no Inspector.");
                return;
            }

            var cancellation = new CancellationTokenSource();
            queryCancellation = cancellation;
            try
            {
                var service = new SupabaseService(settings.CreateConnection(), () => accessToken);
                var repository = new ParticipantRepository(service);
                var participants = await repository.GetAllAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                Debug.Log(participants.Count == 0
                    ? "[Participantes] Nenhum participante visível à identidade atual. Confira os cadastros e RLS."
                    : "[Participantes] Consulta concluída. Registros recebidos: " + participants.Count);
            }
            catch (OperationCanceledException) { }
            catch (SupabaseRequestException ex)
            {
                if (!cancellation.IsCancellationRequested) Debug.LogWarning("[Participantes] " + ex.Message);
            }
            catch (ArgumentException)
            {
                if (!cancellation.IsCancellationRequested)
                    Debug.LogWarning("[Participantes] Configuração inválida. Confira URL, chave pública e tipo do JWT.");
            }
            catch (Exception)
            {
                if (!cancellation.IsCancellationRequested)
                    Debug.LogWarning("[Participantes] Não foi possível concluir a consulta. Confira a configuração.");
            }
            finally
            {
                if (ReferenceEquals(queryCancellation, cancellation)) queryCancellation = null;
                cancellation.Dispose();
            }
        }

        private void OnDisable()
        {
            queryCancellation?.Cancel();
            accessToken = null;
        }
    }
}
