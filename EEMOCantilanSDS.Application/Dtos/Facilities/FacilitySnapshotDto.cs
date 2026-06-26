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
    int CollectionRate,
    // Actual paid collection transactions this month: NPM = paid daily collections (daily-fee ÷ ₱30),
    // monthly facilities = paid + partially-paid stalls. Lets the dashboard's "Paid transactions" KPI
    // match the Financial Reports' "paid collection records" exactly.
    int PaidTransactions = 0
);
