using System.Data;
using System.Data.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// Computes the storage footprint of the CALLER'S municipality only. Every table queried is a
/// <see cref="IMunicipalityOwned"/> table and is filtered by <c>MunicipalityId = @mid</c>, where
/// <c>@mid</c> is <see cref="AppDbContext.CurrentMunicipalityId"/> — the authenticated caller's tenant
/// (resolved server-side from the JWT, never from client input). This NEVER reads another tenant's rows
/// nor the whole-database size (that stays in the platform-operator-only <c>DatabaseHealthRepository</c>).
///
/// Size is estimated via <c>pg_column_size(row)</c> summed over the tenant's rows (heap row bytes; it
/// excludes index/TOAST-external overhead, so it is a lower-bound estimate — labelled as such in the UI).
/// Each table is wrapped defensively so one failure degrades that single row to zero, never the snapshot.
/// </summary>
public class TenantUsageRepository(AppDbContext context) : ITenantUsageRepository
{
    // Friendly labels for the tenant-owned tables; anything unmapped falls back to the raw table name.
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Facilities"] = "Facilities",
        ["FacilityRates"] = "Facility rates",
        ["OrSeriesConfigs"] = "OR series config",
        ["Stalls"] = "Stalls",
        ["Contracts"] = "Contracts",
        ["PaymentRecords"] = "Payment records",
        ["DailyCollections"] = "Daily collections",
        ["UtilityBills"] = "Utility bills",
        ["StallMonthlyExceptions"] = "Monthly exceptions",
        ["NpmMarketClosures"] = "Market closures",
        ["OnlinePaymentTransactions"] = "Online payments",
        ["SlaughterTransactions"] = "Slaughter transactions",
        ["SlaughterAnimalRates"] = "Slaughter rates",
        ["TpmVendors"] = "Tabo-an vendors",
        ["TpmAttendances"] = "Tabo-an attendance",
        ["TrmTransporters"] = "Terminal transporters",
        ["TrmTrips"] = "Terminal trips",
        ["Users"] = "User accounts",
        ["PayorActivationCodes"] = "Payor activation codes",
        ["PayorStallLinks"] = "Payor–stall links",
        ["AuditLogs"] = "Audit log",
        ["CollectorFacilityAssignments"] = "Collector assignments",
    };

    public async Task<TenantUsageDto> GetUsageAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var mid = context.CurrentMunicipalityId;

        // No resolved tenant (bare/anonymous context) → report nothing rather than risk an unscoped read.
        if (mid == Guid.Empty)
            return new TenantUsageDto(0, 0, Array.Empty<TenantUsageItem>(), now);

        // Distinct tenant-owned tables straight from the EF model (root types only, so the TPH user table
        // is counted once). Table/schema names come from trusted EF metadata, never user input.
        var tables = context.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(IMunicipalityOwned).IsAssignableFrom(t.ClrType))
            .Select(t => (Schema: t.GetSchema() ?? "public", Table: t.GetTableName()))
            .Where(x => !string.IsNullOrEmpty(x.Table))
            .Distinct()
            .ToList();

        var connection = context.Database.GetDbConnection();
        var openedHere = false;
        var items = new List<TenantUsageItem>();
        long totalRecords = 0, totalBytes = 0;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
                openedHere = true;
            }

            foreach (var (schema, table) in tables)
            {
                var (count, bytes) = await TryTableUsageAsync(connection, schema!, table!, mid, ct);
                if (count == 0 && bytes == 0) continue;   // skip empty categories from the breakdown

                totalRecords += count;
                totalBytes += bytes;
                items.Add(new TenantUsageItem(
                    Labels.TryGetValue(table!, out var label) ? label : table!, count, bytes));
            }
        }
        catch
        {
            // A connection failure still yields a well-formed (empty) snapshot rather than throwing.
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                try { await connection.CloseAsync(); } catch { /* best-effort */ }
            }
        }

        // Largest categories first for a readable panel.
        var ordered = items.OrderByDescending(i => i.EstimatedSizeBytes).ThenByDescending(i => i.Records).ToList();
        return new TenantUsageDto(totalBytes, totalRecords, ordered, now);
    }

    private static async Task<(long Count, long Bytes)> TryTableUsageAsync(
        DbConnection connection, string schema, string table, Guid mid, CancellationToken ct)
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                $"SELECT COUNT(*), COALESCE(SUM(pg_column_size(t.*)), 0) " +
                $"FROM \"{schema}\".\"{table}\" AS t WHERE t.\"MunicipalityId\" = @mid;";
            var p = cmd.CreateParameter();
            p.ParameterName = "mid";
            p.Value = mid;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var count = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0));
                var bytes = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1));
                return (count, bytes);
            }
        }
        catch
        {
            // Missing column/table or a restricted role → treat this category as empty, never break the snapshot.
        }

        return (0, 0);
    }
}
