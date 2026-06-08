namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// Lean per-facility month figures for the dashboard cards, computed by the canonical
/// (daily-aware) facility-reports aggregation so the dashboard always matches the facility
/// page and reports. Avoids recomputing trends/sections that the dashboard does not need.
/// </summary>
public sealed record FacilitySnapshotDto(
    decimal Collected,
    decimal Pending,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int OccupiedStalls,
    int CollectionRate
);
