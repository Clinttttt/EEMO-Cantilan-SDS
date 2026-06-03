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
    IReadOnlyList<StallComplianceDto> StallCompliance
);
