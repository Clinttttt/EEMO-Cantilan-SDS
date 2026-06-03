using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Dashboard;

public record DashboardOverviewDto(
    decimal TotalCollected,
    decimal TotalPending,
    int UnpaidCount,
    int PaidCount,
    int CollectionRate,
    int ActiveFacilitiesCount,
    int TotalCollectors,
    IReadOnlyList<DashboardFacilityDto> Facilities,
    IReadOnlyList<DashboardTransactionDto> RecentTransactions,
    IReadOnlyList<DashboardDelinquentDto> DelinquentVendors
);

public record DashboardFacilityDto(
    FacilityCode Code,
    string Name,
    decimal Collected,
    int UnpaidCount,
    int TotalVendors,
    int CollectionRate
);

public record DashboardTransactionDto(
    string ORNumber,
    string PayorName,
    FacilityCode FacilityCode,
    decimal Amount,
    string CollectorName,
    DateTime CollectedAt
);

public record DashboardDelinquentDto(
    string Name,
    string StallNo,
    FacilityCode FacilityCode,
    int MonthsUnpaid,
    decimal Balance
);
