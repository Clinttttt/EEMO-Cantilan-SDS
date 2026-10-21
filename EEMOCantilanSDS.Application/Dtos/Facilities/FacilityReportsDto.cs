namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FacilityReportsDto(
    decimal TotalRevenue,
    decimal RevenueGrowthPercentage,
    decimal CollectionRate,
    decimal CollectionGrowthPercentage,
    int OccupiedStalls,
    int TotalStalls,
    int PendingPaymentCount,
    decimal PendingPaymentAmount,
    IReadOnlyList<RevenueTrendDto> RevenueTrend,
    PaymentStatusDistributionDto PaymentDistribution,
    IReadOnlyList<SectionBreakdownDto> SectionBreakdown,
    IReadOnlyList<TopStallDto> TopStalls,
    CollectionPerformanceDto CollectionPerformance,
    DailyCollectionStreakDto? DailyCollectionStreak,
    FeeTypeBreakdownDto? FeeTypeBreakdown,
    IReadOnlyList<FishKiloTrendDto> FishKiloTrend,
    IReadOnlyList<StallComplianceDto> StallCompliance,
    MonitoringTotalsDto? MonitoringTotals = null,
    // False for a Public Market its LGU rents by the month (rent is read from the monthly source, there is no daily ledger); true for every
    // daily-collected market and every other facility, which is what every report built before this field existed meant.
    bool BillsDaily = true
);

public sealed record MonitoringTotalsDto(decimal Assessed, decimal Collected, decimal Balance);
