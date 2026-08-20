namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// What an LGU's own backup consists of: which of its tables travel in a scoped export, which of them a
/// scoped restore may replace, and which are deliberately outside both — each with the reason.
///
/// <para>
/// This was two lists in two files that a comment asked future readers to keep "mirroring" each other, and
/// a set of silent omissions. The real hazard is neither: it is the table nobody remembers. Every table an
/// LGU owns is tenant-filtered by construction, so a new one arrives already isolated and already invisible
/// to this list — its rows would simply never appear in the office's backup, and nothing would say so. The
/// office would find out at the only moment that matters. So the sets are stated here once, and
/// <c>TenantBackupCoverageTests</c> holds the model against them: every tenant-owned entity must be
/// restorable, export-only, or named below with a reason. Adding a tenant table and forgetting its backup
/// now fails the build instead of the restore.
/// </para>
/// </summary>
public static class TenantDataTables
{
    /// <summary>
    /// The office's operational and financial record — the tables a scoped restore DELETEs and re-INSERTs
    /// for the caller's municipality alone. Order is irrelevant here; the restore derives foreign-key order
    /// from the model.
    /// </summary>
    public static readonly IReadOnlySet<string> Restorable = new HashSet<string>(StringComparer.Ordinal)
    {
        "Facilities", "FacilityRates", "OrSeriesConfigs", "Stalls", "Contracts", "PaymentRecords",
        "DailyCollections", "UtilityBills", "StallMonthlyExceptions", "NpmMarketClosures",
        "OnlinePaymentTransactions", "SlaughterTransactions", "SlaughterAnimalRates", "TpmVendors",
        "TpmAttendances", "TrmTransporters", "TrmTrips", "PayorStallLinks", "CollectorFacilityAssignments",
    };

    /// <summary>
    /// Carried in the export, never replaced by a restore. The audit trail is append-only: it is the record
    /// of what was done to the data, including the restore itself, so restoring over it would let an action
    /// erase the evidence of that action.
    /// </summary>
    public static readonly IReadOnlySet<string> ExportOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        "AuditLogs",
    };

    /// <summary>
    /// Tables an LGU owns that are in neither set, and why. Each entry is a decision, not an oversight — a
    /// backup that quietly leaves out part of an office's record is worse than one that says what it holds.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotBackedUp = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Users"] =
            "Credentials. Accounts, password hashes and second-factor enrolments are never exported and never "
            + "replaced: a restored snapshot must not be able to reinstate a removed account or an old password.",
        ["PayorActivationCodes"] =
            "One-time secrets. A vendor's app activation code is spent on use and expires; restoring one would "
            + "revive a credential the office already retired.",
        ["CollectorDeviceTokens"] =
            "Per-device push registrations, issued by the device and valid only while it holds them. Restored "
            + "tokens would be stale on arrival, addressing handsets that no longer answer to them.",
        ["HiddenSuggestions"] =
            "A per-screen dismissal — which prompt a clerk has waved away. It carries no record of collection.",
        ["TenantBackups"] =
            "The office's own backup history. Excluding it is what keeps a restore from erasing the backups the "
            + "office would need if that restore turns out to be the wrong one.",
    };
}
