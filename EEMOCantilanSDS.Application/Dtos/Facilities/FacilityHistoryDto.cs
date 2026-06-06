namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>One collection-history row — a single month or a single year.</summary>
public record FacilityHistoryPeriodDto(
    string Label,          // "January" for monthly rows, "2024" for yearly rows
    int Year,
    int? Month,            // null for yearly rows
    int TotalStalls,
    decimal Collected,
    decimal Outstanding,
    int FollowUp,
    decimal CollectionRate
);

/// <summary>
/// Collection history for a facility: the 12 months of the selected year plus a rolling
/// 5-year summary. Each row carries the same figures the per-period report shows.
/// </summary>
public record FacilityHistoryDto(
    int Year,
    IReadOnlyList<FacilityHistoryPeriodDto> Monthly,
    IReadOnlyList<FacilityHistoryPeriodDto> Yearly
);
