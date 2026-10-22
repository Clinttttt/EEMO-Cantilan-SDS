using System.Data.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>The one disposable preview an Office rent-receipt test may be enabled for.</summary>
public sealed record PreviewMonthlyRentTarget(string Database, int Port, Guid Tenant, FacilityCode Facility);

/// <summary>
/// Fail-closed gate for the preview-only canonical rent test. It never activates anything by itself: it only decides
/// whether the exact disposable database, tenant and facility may receive <see cref="MonthlyRentReceiptOptions"/>, and
/// whether one source row is a fresh synthetic month that may enter the real cutover lifecycle. Any doubt returns refusals.
/// </summary>
public static class PreviewMonthlyRentGate
{
    public const string SyntheticStallPrefix = "PREVIEW-";

    /// <summary>The only synthetic stall identities the preview tool may create or activate. Adding one is a reviewed code change.</summary>
    public static readonly IReadOnlySet<string> PermittedStalls = new HashSet<string>(StringComparer.Ordinal) {
        "PREVIEW-NCC-01", "PREVIEW-NCC-02", "PREVIEW-NCC-03",
        // A synthetic Public Market declared as standard monthly rental (reviewed 2026-10-18); the activation tool still refuses a daily market.
        "PREVIEW-NPM-01", "PREVIEW-NPM-02", "PREVIEW-NPM-03", "PREVIEW-NPM-04", "PREVIEW-NPM-05", "PREVIEW-NPM-06"
    };

    /// <summary>Loopback only, exact database name and port. A missing or unparsable connection string refuses.</summary>
    public static IReadOnlyList<string> DatabaseRefusals(string? connectionString, string expectedDatabase, int expectedPort)
    {
        var refusals = new List<string>();
        if (string.IsNullOrWhiteSpace(expectedDatabase) || expectedPort <= 0)
            return ["The preview database name and port must both be stated."];
        // The retained municipal database is never an acceptable target for any preview or working-copy gate.
        if (expectedPort == 5432 || string.Equals(expectedDatabase, "EEMOCantilanSDS.DB", StringComparison.OrdinalIgnoreCase))
            return ["The retained municipal database is never a preview or working-copy target."];
        if (string.IsNullOrWhiteSpace(connectionString))
            return ["No connection string is available to verify."];
        DbConnectionStringBuilder b;
        try { b = new DbConnectionStringBuilder { ConnectionString = connectionString }; }
        catch (ArgumentException) { return ["The connection string cannot be verified."]; }

        string Get(params string[] keys) => keys.Select(k => b.TryGetValue(k, out var v) ? v?.ToString() : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        if (!string.Equals(Get("Database", "Initial Catalog"), expectedDatabase, StringComparison.Ordinal))
            refusals.Add("The connection does not target the exact disposable preview database.");
        if (Get("Host", "Server", "Data Source") is not ("localhost" or "127.0.0.1" or "::1"))
            refusals.Add("The preview database must be on this machine (loopback host).");
        if (!int.TryParse(Get("Port"), out var port) || port != expectedPort)
            refusals.Add("The connection does not use the preview database port.");
        return refusals;
    }

    /// <summary>Options for the running API: the default (everything off) unless every identity check passes.</summary>
    public static MonthlyRentReceiptOptions OptionsFor(
        bool enabled, PreviewMonthlyRentTarget? target, string? connectionString, out IReadOnlyList<string> refusals)
    {
        if (!enabled)
        {
            refusals = [];
            return new MonthlyRentReceiptOptions();
        }
        var found = new List<string>();
        if (target is null || target.Tenant == Guid.Empty)
            found.Add("An explicit preview tenant is required.");
        else
        {
            found.AddRange(DatabaseRefusals(connectionString, target.Database, target.Port));
            if (target.Facility is not (FacilityCode.NCC or FacilityCode.TCC or FacilityCode.BBQ or FacilityCode.ICE or FacilityCode.NPM))
                found.Add("Only a monthly-rental facility may be enabled.");
        }
        refusals = found;
        return found.Count > 0
            ? new MonthlyRentReceiptOptions()
            : new MonthlyRentReceiptOptions(true,
                new HashSet<FacilityCode> { target!.Facility }, new HashSet<Guid> { target.Tenant });
    }

    /// <summary>A source may enter Begin -> Freeze -> Activate only if it is a fresh, synthetic, unpaid, zero-cash Legacy month.</summary>
    public static IReadOnlyList<string> SourceRefusals(
        PreviewMonthlyRentTarget target, Guid sourceTenant, FacilityCode sourceFacility, string stallNumber,
        SettlementAuthority authority, PaymentStatus status, decimal legacySettled, bool hasCutover, bool hasCollection)
    {
        var refusals = new List<string>();
        if (sourceTenant != target.Tenant) refusals.Add("The source belongs to a different tenant than the reviewed preview tenant.");
        if (sourceFacility != target.Facility) refusals.Add("The source is not in the reviewed facility.");
        if (!PermittedStalls.Contains(stallNumber))
            refusals.Add("Only an explicitly permitted synthetic preview stall may be activated; existing stalls and their legacy history are never converted.");
        if (authority != SettlementAuthority.Legacy)
            refusals.Add("The source is not Legacy; it has already been activated or is mid-cutover.");
        if (status != PaymentStatus.Unpaid || legacySettled != 0m)
            refusals.Add("The source month is not unpaid with zero legacy collected cash.");
        if (hasCutover) refusals.Add("An opening settlement is already frozen for this source.");
        if (hasCollection) refusals.Add("Canonical collections already reference this source.");
        return refusals;
    }
}
