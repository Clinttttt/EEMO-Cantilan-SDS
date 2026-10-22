using System.Data;
using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

public sealed record RestoreColumn(string Name, string StoreType, bool Nullable, bool Generated);

/// <summary>Server-owned physical schema evidence; never supplied by the uploaded snapshot.</summary>
public sealed record TenantRestoreSchema(
    string TenantCode,
    IReadOnlyDictionary<string, IReadOnlyList<RestoreColumn>> Tables,
    IReadOnlyList<string> UnmanagedTables,
    long SrcLastValue,
    bool SrcIsCalled)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(Tables.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new { Table = t.Key, Columns = t.Value.OrderBy(c => c.Name, StringComparer.Ordinal) })))));

    public void RequireRestorable()
    {
        if (UnmanagedTables.Count != 0)
            throw new InvalidOperationException($"Scoped restore is unavailable: retained or unmanaged tables require an approved full-database recovery strategy. Unmanaged: {string.Join(", ", UnmanagedTables)}.");
    }
}

public interface ITenantRestoreSchemaReader
{
    Task<TenantRestoreSchema> ReadAsync(CancellationToken ct);
}

/// <summary>Reads catalogs only. Missing pending NPM schema is distinguished from an empty migrated table.</summary>
public sealed class TenantRestoreSchemaReader(AppDbContext context) : ITenantRestoreSchemaReader
{
    public async Task<TenantRestoreSchema> ReadAsync(CancellationToken ct)
    {
        if (context.CurrentMunicipalityId == Guid.Empty)
            throw new InvalidOperationException("No municipality is resolved for this request.");

        // Exact-ID lookup under the existing tenant/soft-delete filters; no cross-tenant bypass.
        var tenantCode = await context.Municipalities.AsNoTracking()
            .Where(m => m.Id == context.CurrentMunicipalityId).Select(m => m.TenantCode).SingleAsync(ct);
        var modelTables = context.Model.GetRelationalModel().Tables.Where(t => t.Schema is null or "public").ToList();
        var required = modelTables.Where(t => TenantDataTables.Restorable.Contains(t.Name)).ToList();
        var physical = new Dictionary<string, IReadOnlyList<RestoreColumn>>(StringComparer.Ordinal);

        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                cmd.CommandText = """
                    SELECT c.relname, a.attname, format_type(a.atttypid,a.atttypmod),
                           NOT a.attnotnull, a.attgenerated <> '' OR a.attidentity <> ''
                    FROM pg_catalog.pg_class c
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
                    WHERE n.nspname='public' AND c.relkind IN ('r','p') ORDER BY c.relname,a.attnum
                    """;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                var columns = new Dictionary<string, List<RestoreColumn>>(StringComparer.Ordinal);
                while (await reader.ReadAsync(ct))
                {
                    var table = reader.GetString(0);
                    if (!columns.TryGetValue(table, out var list)) columns[table] = list = [];
                    list.Add(new(reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4)));
                }
                foreach (var entry in columns) physical.Add(entry.Key, entry.Value);
            }

            var available = new Dictionary<string, IReadOnlyList<RestoreColumn>>(StringComparer.Ordinal);
            foreach (var table in required)
            {
                if (!physical.TryGetValue(table.Name, out var columns))
                {
                    if (table.Name == "NpmRentMonths") continue; // Preparation model, not yet migrated.
                    throw new InvalidOperationException("The database is missing a required backup table; restore/export is unavailable.");
                }
                if (!table.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(columns.Select(c => c.Name)))
                    throw new InvalidOperationException("The database table shape is incompatible with the backup model.");
                available.Add(table.Name, columns);
            }

            var known = modelTables.Select(t => t.Name).Append("__EFMigrationsHistory").ToHashSet(StringComparer.Ordinal);
            var unmanaged = physical.Keys.Where(t => !known.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToArray();
            await using var sequence = connection.CreateCommand();
            sequence.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            sequence.CommandText = "SELECT last_value, is_called FROM public.\"CollectionReferenceNumberSeq\"";
            await using var sequenceReader = await sequence.ExecuteReaderAsync(ct);
            await sequenceReader.ReadAsync(ct);
            return new(tenantCode, available, unmanaged, sequenceReader.GetInt64(0), sequenceReader.GetBoolean(1));
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
