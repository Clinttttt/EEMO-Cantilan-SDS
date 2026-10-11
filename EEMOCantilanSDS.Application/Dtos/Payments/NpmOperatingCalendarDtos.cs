using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>One day of the New Public Market's operating calendar and the reason for its state.</summary>
/// <param name="Reason">Why an explicit closure was recorded; null for a scheduled or ordinary day.</param>
/// <param name="HasExplicitRow">True when the Head has overridden this day, so it can be restored to the schedule.</param>
public sealed record NpmOperatingDayDto(DateOnly Date, NpmDayState State, string? Reason, string? Remarks, bool HasExplicitRow);

/// <summary>A weekly operating pattern and the date it takes effect.</summary>
public sealed record NpmOperatingScheduleDto(DateOnly EffectiveFrom, IReadOnlyList<DayOfWeek> ClosedWeekdays, string? Remarks, string? SetBy);

/// <summary>A month of the calendar, with the schedule versions that explain its weekly closures.</summary>
public sealed record NpmOperatingCalendarDto(
    int Year, int Month, IReadOnlyList<NpmOperatingDayDto> Days, IReadOnlyList<NpmOperatingScheduleDto> Schedules,
    NpmOperatingScheduleDto? InForce, int ClosedDays, int OpenDays);

/// <summary>What changing one day would affect, shown before the Head confirms.</summary>
public sealed record NpmDayImpactDto(DateOnly Date, bool IsPast, int BillableAccounts, int UnpaidAccounts, int PaidAccounts, decimal UnpaidAmount);
