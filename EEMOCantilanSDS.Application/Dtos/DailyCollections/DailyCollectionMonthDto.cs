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
    Dictionary<string, DailyCollectionDayDto> Collections
);

public sealed record DailyCollectionDayDto(
    DateOnly Date,
    bool IsPaid,
    decimal? FishKilos
);
