using System.Globalization;
using System.Text.Json;
using EEMOCantilanSDS.Application.Dtos.Backup;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>Pure pre-delete checks. The manifest and column shape come exclusively from the server.</summary>
public static class TenantRestoreValidation
{
    public static TenantRestoreSnapshot FreezeAndValidateHeader(TenantRestoreSnapshot snapshot, Guid municipalityId)
    {
        if (municipalityId == Guid.Empty) throw new InvalidOperationException("No municipality is resolved for this request.");
        if (snapshot is null || snapshot.Tables is null) throw new InvalidOperationException("The restore snapshot or table manifest is missing.");
        if (snapshot.FormatVersion != TenantRestoreSnapshot.CurrentFormatVersion)
            throw new InvalidOperationException("Only restore-v2 snapshots are accepted; legacy snapshots require an approved recovery procedure.");
        if (snapshot.MunicipalityId != municipalityId)
            throw new InvalidOperationException("This backup belongs to a different municipality.");
        return snapshot with { Tables = new(snapshot.Tables, StringComparer.Ordinal) };
    }

    public static void Validate(TenantRestoreSnapshot snapshot, Guid municipalityId, TenantRestoreSchema schema)
    {
        FreezeAndValidateHeader(snapshot, municipalityId);
        schema.RequireRestorable();
        if (snapshot.TenantCode != schema.TenantCode) throw new InvalidOperationException("The backup tenant identity does not match the server.");
        if (snapshot.SchemaFingerprint != schema.Fingerprint) throw new InvalidOperationException("The backup schema fingerprint is incompatible with this database.");
        if (!snapshot.Tables.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(schema.Tables.Keys))
            throw new InvalidOperationException("The backup must contain the complete server table manifest, including explicit empty arrays.");

        foreach (var (table, columns) in schema.Tables)
        {
            try
            {
                using var json = JsonDocument.Parse(snapshot.Tables[table]);
                if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("A backup table must be a JSON array.");
                var ids = new HashSet<Guid>();
                foreach (var row in json.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("A backup row must be an object.");
                    var names = row.EnumerateObject().Select(p => p.Name).ToArray();
                    if (names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                        !names.ToHashSet(StringComparer.Ordinal).SetEquals(columns.Select(c => c.Name)))
                        throw new InvalidOperationException("A backup row has missing, duplicate or unknown columns.");
                    if (!row.TryGetProperty("MunicipalityId", out var tenant) || tenant.ValueKind != JsonValueKind.String ||
                        !tenant.TryGetGuid(out var mid) || mid != municipalityId)
                        throw new InvalidOperationException("A backup row belongs to a different municipality.");
                    foreach (var column in columns)
                        ValidateValue(row.GetProperty(column.Name), column);
                    if (row.TryGetProperty("Id", out var id) &&
                        (id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var guid) || guid == Guid.Empty || !ids.Add(guid)))
                        throw new InvalidOperationException("A backup row has an invalid or duplicate identifier.");
                    if (table == "Collections") ValidateSrc(row, schema);
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                throw new InvalidOperationException("A backup table contains malformed JSON.", ex);
            }
        }
    }

    private static void ValidateValue(JsonElement value, RestoreColumn column)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!column.Nullable) throw new InvalidOperationException("A backup row is missing a required value.");
            return;
        }
        var type = column.StoreType;
        var valid = type switch
        {
            "uuid" => value.ValueKind == JsonValueKind.String && value.TryGetGuid(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "bigint" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "smallint" => value.ValueKind == JsonValueKind.Number && value.TryGetInt16(out _),
            "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "json" or "jsonb" => true,
            "real" or "double precision" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d),
            _ => false
        };
        if (type.StartsWith("numeric", StringComparison.Ordinal))
        {
            // PostgreSQL row_to_json emits numeric values in plain decimal notation. Inspect the
            // digits rather than rounding through System.Decimal or a SQL numeric(p,s) cast.
            var raw = value.GetRawText();
            valid = value.ValueKind == JsonValueKind.Number &&
                System.Text.RegularExpressions.Regex.IsMatch(raw, @"^-?\d+(\.\d+)?$");
            if (valid && type.Contains('('))
            {
                var parts = type[(type.IndexOf('(') + 1)..^1].Split(',');
                var precision = int.Parse(parts[0], CultureInfo.InvariantCulture);
                var scale = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
                var digits = raw.TrimStart('-').Split('.');
                valid = scale >= 0 && digits[0].TrimStart('0').Length <= precision - scale &&
                    (digits.Length == 1 || digits[1].Length <= scale);
            }
        }
        else if (type is "text" or "bytea" or "interval" || type.StartsWith("timestamp", StringComparison.Ordinal) ||
                 type.StartsWith("character", StringComparison.Ordinal))
        {
            valid = value.ValueKind == JsonValueKind.String;
            if (valid && type.StartsWith("character", StringComparison.Ordinal) && type.Contains('('))
            {
                var length = int.Parse(type[(type.IndexOf('(') + 1)..^1], CultureInfo.InvariantCulture);
                valid = value.GetString()!.EnumerateRunes().Count() <= length;
            }
        }
        else if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            valid = value.ValueKind == JsonValueKind.Array;
            if (valid)
                foreach (var item in value.EnumerateArray()) ValidateValue(item, column with { StoreType = type[..^2], Nullable = true });
        }
        if (!valid) throw new InvalidOperationException("A backup value is incompatible with its server column type.");
    }

    private static void ValidateSrc(JsonElement row, TenantRestoreSchema schema)
    {
        if (!row.TryGetProperty("ReferenceNumber", out var number) || !number.TryGetInt64(out var value) || value <= 0 ||
            value > schema.SrcLastValue || (!schema.SrcIsCalled && value == schema.SrcLastValue))
            throw new InvalidOperationException("The backup requires an SRC sequence recovery; scoped restore never advances the sequence.");
        if (!row.TryGetProperty("ReferenceYear", out var year) || !year.TryGetInt32(out var y) ||
            !row.TryGetProperty("ReferenceCode", out var code) || code.GetString() != $"SRC-{y}-{value.ToString("D6", CultureInfo.InvariantCulture)}")
            throw new InvalidOperationException("The backup contains incompatible SRC evidence.");
    }
}
