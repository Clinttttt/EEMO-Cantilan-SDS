namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record RevenueTrendDto(
    string PeriodLabel,
    decimal Revenue,
    decimal ExpectedRevenue = 0m,
    bool IsCurrentPeriod = false,
    // NPM only: the fish-kilo (₱1/kg) portion of Revenue for this period, so the trend bar can split
    // the total into daily-rent vs fish-fee segments. 0 for facilities without a fish fee.
    decimal FishFeeRevenue = 0m
);
