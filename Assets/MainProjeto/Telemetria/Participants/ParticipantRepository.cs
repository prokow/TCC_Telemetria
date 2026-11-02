using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MainProjeto.Telemetria.Supabase;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria.Participants
{
    public sealed class ParticipantRepository
    {
        private readonly ISupabaseReadClient client;
        private readonly int pageSize;
        private readonly int maxParticipants;

        public ParticipantRepository(ISupabaseReadClient client, int pageSize = 100, int maxParticipants = 10000)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            if (pageSize < 1 || pageSize > 1000) throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (maxParticipants < 1) throw new ArgumentOutOfRangeException(nameof(maxParticipants));
            this.pageSize = pageSize;
            this.maxParticipants = maxParticipants;
        }

        public async Task<IReadOnlyList<Participant>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var participants = new List<Participant>();
            var codes = new HashSet<string>(StringComparer.Ordinal);
            string cursor = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string query = "participants?select=participant_code,display_name&order=participant_code.asc&limit=" +
                               pageSize.ToString(CultureInfo.InvariantCulture);
                if (cursor != null) query += "&participant_code=gt." + Uri.EscapeDataString(cursor);

                string json = await client.GetAsync(query, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                List<Participant> page = ParsePage(json);
                // Continuar até uma página vazia mesmo se o servidor limitar a página a menos linhas.
                if (page.Count == 0) return participants.AsReadOnly();
                foreach (Participant participant in page)
                {
                    if (!codes.Add(participant.ParticipantCode))
                        throw InvalidResponse("A paginação retornou um participante repetido. Atualize a lista.");
                    if (participants.Count >= maxParticipants)
                        throw InvalidResponse("A lista excedeu o limite configurado. Nenhuma lista parcial será utilizada.");
                    participants.Add(participant);
                }
                cursor = page[page.Count - 1].ParticipantCode;
            }
        }

        private static List<Participant> ParsePage(string json)
        {
            try
            {
                // Exigir array e tipos corretos: HTTP 200 não garante um contrato de dados válido.
                var rows = JArray.Parse(json ?? string.Empty);
                var page = new List<Participant>(rows.Count);
                foreach (JToken token in rows)
                {
                    if (!(token is JObject row) || row["participant_code"]?.Type != JTokenType.String)
                        throw InvalidResponse("Resposta sem participant_code válido.");
                    JToken name = row["display_name"];
                    if (name == null || (name.Type != JTokenType.Null && name.Type != JTokenType.String))
                        throw InvalidResponse("Resposta sem display_name válido.");
                    page.Add(new Participant((string)row["participant_code"], (string)name));
                }
                return page;
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
            {
                // Não anexar a exceção de parsing: ela pode reproduzir o corpo contendo dados pessoais.
                throw InvalidResponse("Resposta inválida: esperado participant_code GUID N e display_name texto ou nulo.");
            }
        }

        private static SupabaseRequestException InvalidResponse(string message)
        {
            return new SupabaseRequestException(SupabaseFailure.InvalidResponse, message);
        }
    }
}
