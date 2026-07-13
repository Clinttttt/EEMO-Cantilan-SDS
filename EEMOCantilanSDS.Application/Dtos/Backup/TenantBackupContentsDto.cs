namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// The inspectable contents of one stored backup: its metadata plus a per-table manifest (record count
/// per table). Lets a Head see exactly what a backup holds before restoring it — no guessing.
/// </summary>
public sealed record TenantBackupContentsDto(
    Guid Id,
    DateTime CreatedAtUtc,
    string CreatedBy,
    int RowCount,
    int TableCount,
    long SizeBytes,
    string? Note,
    IReadOnlyList<TenantBackupTableDto> Tables);

/// <summary>One line of a backup's manifest: a table, its friendly label, and how many records it holds.</summary>
public sealed record TenantBackupTableDto(string Table, string DisplayName, int RowCount);

/// <summary>Humanises the internal table names in a backup manifest into government-readable labels.</summary>
public static class TenantBackupTableNames
{
    private static readonly IReadOnlyDictionary<string, string> Friendly = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Facilities"] = "Facilities",
        ["FacilityRates"] = "Facility rates",
        ["OrSeriesConfigs"] = "OR series configuration",
        ["Stalls"] = "Stalls",
        ["Contracts"] = "Contracts",
        ["PaymentRecords"] = "Payment records",
        ["DailyCollections"] = "Daily collections",
        ["UtilityBills"] = "Utility bills",
        ["StallMonthlyExceptions"] = "Stall monthly exceptions",
        ["NpmMarketClosures"] = "Market closures",
        ["OnlinePaymentTransactions"] = "Online payments",
        ["SlaughterTransactions"] = "Slaughterhouse transactions",
        ["SlaughterAnimalRates"] = "Slaughterhouse rates",
        ["TpmVendors"] = "Taboan vendors",
        ["TpmAttendances"] = "Taboan attendance",
        ["TrmTransporters"] = "Terminal transporters",
        ["TrmTrips"] = "Terminal trips",
        ["PayorStallLinks"] = "Payor–stall links",
        ["CollectorFacilityAssignments"] = "Collector assignments",
    };

    public static string Display(string table)
    {
        if (Friendly.TryGetValue(table, out var label)) return label;
        // Fallback: split PascalCase into spaced words so an unmapped table is still readable.
        if (string.IsNullOrEmpty(table)) return table;
        var sb = new System.Text.StringBuilder(table.Length + 4);
        for (var i = 0; i < table.Length; i++)
        {
            var c = table[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(table[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
