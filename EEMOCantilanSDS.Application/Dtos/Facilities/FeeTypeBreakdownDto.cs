namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FeeTypeBreakdownDto(
    decimal DailyFeeAmount,
    decimal FishFeeAmount,
    string? FishKiloComparison,
    /// <summary>
    /// How many collections were actually recorded in the period: one per daily collection, plus the stall-days
    /// a monthly payment covered. Counted where each stall's own daily fee is known, because inferring it by
    /// dividing money by one facility-wide rate mis-counts a custom section that charges its own rate — and any
    /// month carrying a month-end adjustment.
    /// </summary>
    int PaidDayRecords = 0,
    /// <summary>The collectable stall-days the same period expected, counted on the same basis.</summary>
    int ExpectedDayRecords = 0
);
