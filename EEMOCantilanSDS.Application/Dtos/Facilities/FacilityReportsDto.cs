namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FacilityReportsDto(
    decimal TotalRevenue,
    decimal RevenueGrowthPercentage,
    decimal CollectionRate,
    decimal CollectionGrowthPercentage,
    int OccupiedStalls,
    int PendingPaymentCount,
    decimal PendingPaymentAmount,
    IReadOnlyList<RevenueTrendDto> RevenueTrend,
    PaymentStatusDistributionDto PaymentDistribution,
    IReadOnlyList<SectionBreakdownDto> SectionBreakdown,
    IReadOnlyList<TopStallDto> TopStalls,
    CollectionPerformanceDto CollectionPerformance
);
