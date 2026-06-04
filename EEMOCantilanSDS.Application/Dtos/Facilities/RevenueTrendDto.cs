namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record RevenueTrendDto(
    string PeriodLabel,
    decimal Revenue,
    decimal ExpectedRevenue = 0m,
    bool IsCurrentPeriod = false
);
