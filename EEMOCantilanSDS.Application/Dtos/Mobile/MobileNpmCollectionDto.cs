using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record MobileNpmCollectionDto(
    int Year,
    int Month,
    DateOnly CollectionDate,
    int TotalStalls,
    int CollectedTodayCount,
    int PendingTodayCount,
    decimal CollectedTodayAmount,
    decimal PendingTodayAmount,
    int MonthCollectedDays,
    int MonthMissedDays,
    IReadOnlyList<MobileNpmStallCollectionDto> Stalls);

public sealed record MobileNpmStallCollectionDto(
    Guid StallId,
    string StallNo,
    string PayorName,
    string ContractName,
    MarketSection? Section,
    string SectionName,
    StallStatus Status,
    decimal DailyRate,
    bool IsMarkedToday,
    bool IsCollectedToday,
    string? ORNumberToday,
    decimal? FishKilosToday,
    int DaysCollected,
    int DaysMissed,
    int CollectableDays,
    decimal MonthCollectedAmount,
    bool IsAbsentToday = false,
    bool IsCollectableToday = false,
    // The elapsed days of this month this payor still owes, earliest first.
    //
    // The DATES, not a count, because a collector settling a day the office missed has to say WHICH day the money answers
    // for. DaysMissed above is a plain subtraction that knows nothing about market closures, so it can be larger than this
    // list; these days are the ones actually owed - within a term that covers them, not closed, and neither collected nor
    // excused already.
    IReadOnlyList<DateOnly>? UncollectedDays = null);

