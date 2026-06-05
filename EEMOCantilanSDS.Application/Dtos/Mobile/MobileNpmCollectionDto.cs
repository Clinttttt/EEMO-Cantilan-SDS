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
    decimal? FishKilosToday,
    int DaysCollected,
    int DaysMissed,
    int CollectableDays,
    decimal MonthCollectedAmount);
