namespace EEMOCantilanSDS.Application.Dtos.DailyCollections;

public sealed record DailyCollectionMonthDto(
    int Year,
    int Month,
    int TotalDays,
    int DaysCollected,
    int DaysMissed,
    decimal TotalDailyFee,
    decimal TotalFishKilos,
    decimal TotalFishFee,
    decimal GrandTotal,
    bool IsFullyPaid,
    Dictionary<string, DailyCollectionDayDto> Collections,
    int DaysAbsent = 0,
    int DaysClosed = 0
);

public sealed record DailyCollectionDayDto(
    DateOnly Date,
    bool IsPaid,
    decimal? FishKilos,
    bool IsAbsent = false,
    bool IsMarketClosed = false,
    string? ORNumber = null,
    /// <summary>
    /// What was actually recorded against this day — the installment as stamped, including any month-end balance
    /// adjustment carried on it. Stated so a receipt written against the day shows the money the office received,
    /// rather than a figure re-derived from today's rate. Zero for a day that was never collected.
    /// </summary>
    decimal AmountCollected = 0m
);
