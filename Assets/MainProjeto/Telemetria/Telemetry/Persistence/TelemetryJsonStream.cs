using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MainProjeto.Telemetria
{
    // Visits one sample at a time; never materializes the session arrays.
    public static class TelemetryJsonStream
    {
        public static TelemetrySessionPayload Read(string path, Action<HeadTelemetryFrame> head = null,
            Action<ControllerTelemetryFrame> controllers = null, Action<TelemetryEventFrame> events = null)
        {
            var metadata = new JObject();
            var serializer = JsonSerializer.CreateDefault();
            using (var text = File.OpenText(path))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 })
            {
                if (!reader.Read() || reader.TokenType != JsonToken.StartObject) throw new InvalidDataException("Sessao JSON invalida.");
                bool hasHead = false, hasControllers = false, hasEvents = false, ended = false;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject) { ended = true; break; }
                    if (reader.TokenType != JsonToken.PropertyName) throw new InvalidDataException("Propriedade JSON invalida.");
                    string name = (string)reader.Value;
                    if (!reader.Read()) throw new InvalidDataException("JSON incompleto.");
                    switch (name)
                    {
                        case "HeadFrames": if (hasHead) throw new InvalidDataException("Array duplicado."); hasHead = true; Visit(reader, serializer, head); break;
                        case "ControllerFrames": if (hasControllers) throw new InvalidDataException("Array duplicado."); hasControllers = true; Visit(reader, serializer, controllers); break;
                        case "Events": if (hasEvents) throw new InvalidDataException("Array duplicado."); hasEvents = true; Visit(reader, serializer, events); break;
                        default:
                            if (metadata.ContainsKey(name)) throw new InvalidDataException("Propriedade duplicada.");
                            if (reader.TokenType == JsonToken.StartArray || reader.TokenType == JsonToken.StartObject) reader.Skip();
                            else metadata[name] = reader.Value == null ? JValue.CreateNull() : new JValue(reader.Value);
                            break;
                    }
                }
                if (!ended || !hasHead || !hasControllers || !hasEvents || reader.Read()) throw new InvalidDataException("Sessao JSON incompleta.");
            }
            return metadata.ToObject<TelemetrySessionPayload>(serializer);
        }
        private static void Visit<T>(JsonTextReader reader, JsonSerializer serializer, Action<T> visitor)
        {
            if (reader.TokenType != JsonToken.StartArray) throw new InvalidDataException("Array de telemetria invalido.");
            if (visitor == null) { reader.Skip(); return; }
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray) return;
                if (reader.TokenType != JsonToken.StartObject) throw new InvalidDataException("Amostra invalida.");
                visitor(serializer.Deserialize<T>(reader));
            }
            throw new InvalidDataException("Array incompleto.");
        }
    }
}