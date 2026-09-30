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
    int ExpectedDayRecords = 0,
    /// <summary>Legacy NPM Meat weighing amount; retained for existing report consumers.</summary>
    decimal WeightMeasureAmount = 0m,
    /// <summary>Meat kilos recorded on paid daily NPM collections for the selected period.</summary>
    decimal MeatKilos = 0m,
    /// <summary>Explicit Meat weighing amount; separate from daily stall rent and Fish weighing.</summary>
    decimal MeatWeightMeasureAmount = 0m,
    /// <summary>Fish weighing money frozen at collection time (rate, effective date and amount); never derived from today's rate.</summary>
    decimal FishWeightMeasureFrozenAmount = 0m,
    /// <summary>Fish kilos with no frozen rate evidence (earlier rows and monthly-payment kilos): unresolved, not priced.</summary>
    decimal FishKilosWithoutFrozenRate = 0m
);
