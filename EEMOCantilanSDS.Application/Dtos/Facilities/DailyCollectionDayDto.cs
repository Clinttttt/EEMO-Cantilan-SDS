namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record DailyCollectionDayDto(
    int DayNumber,
    int DayOfWeek,
    string Status
);
