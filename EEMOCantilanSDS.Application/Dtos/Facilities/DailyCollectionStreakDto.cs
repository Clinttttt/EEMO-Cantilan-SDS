namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record DailyCollectionStreakDto(
    string MonthLabel,
    int CollectedDays,
    int MissedDays,
    int CurrentStreakDays,
    IReadOnlyList<DailyCollectionDayDto> Days
);
