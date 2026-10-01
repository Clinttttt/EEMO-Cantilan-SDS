using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// How a revenue source earns its money, which decides the secondary figures that describe it truthfully. A source paid at
/// the point of service has no assessment, so it never shows an unpaid amount or a collection rate.
/// </summary>
public static class RevenueSourceModel
{
    public const string RecurringObligation = "RecurringObligation";
    public const string Transactional = "Transactional";
    public const string QuantityService = "QuantityService";
    public const string EventRental = "EventRental";
    public const string Receivable = "Receivable";
}

/// <summary>
/// One revenue source's performance for the period. <see cref="Collected"/> is the official Monthly Income row's cash for the
/// same period — legacy before cutover plus canonical after it, exactly once — so this register and the statement cannot
/// disagree. The counts are read from posted Collections and are null (not zero) where the row also carries legacy money
/// they cannot describe.
/// </summary>
public sealed record RevenueSourcePerformanceRowDto(
    string Key,
    string Label,
    string GroupKey,
    string GroupLabel,
    string Model,
    // The approved instrument(s) on the classification policy in force, e.g. "OR", "CT", "OR · CT". Null when not configured.
    string? Instruments,
    // The facility whose register describes this source's accounts and balances (rent rows and Ice Plant only).
    FacilityCode? Facility,
    decimal Collected,
    decimal LegacyCollected,
    decimal CanonicalCollected,
    int? TransactionCount,
    int? DocumentCount,
    int? CollectorCount,
    string Status,
    // True when the source has money but no approved official Monthly Income placement yet.
    bool AwaitingPlacement);

public sealed record RevenueSourceGroupDto(string Key, string Label, decimal Collected);

public sealed record RevenueSourcePerformanceDto(
    int Year,
    int? Month,
    IReadOnlyList<RevenueSourceGroupDto> Groups,
    IReadOnlyList<RevenueSourcePerformanceRowDto> Rows,
    decimal TotalCollected,
    IReadOnlyList<string> Notes,
    DateTime GeneratedAtUtc);
