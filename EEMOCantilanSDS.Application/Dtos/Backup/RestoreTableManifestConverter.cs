using System.Text.Json;
using System.Text.Json.Serialization;

namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>Do not let deserialization silently collapse duplicate or malformed table entries.</summary>
public sealed class RestoreTableManifestConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("A table manifest must be an object.");
        var tables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String ||
                !tables.TryAdd(property.Name, property.Value.GetString()!))
                throw new JsonException("A table manifest contains a duplicate or malformed entry.");
        }
        return tables;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (table, json) in value) writer.WriteString(table, json);
        writer.WriteEndObject();
    }
}
