using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SetNpmOperatingSchedule;

/// <summary>
/// The Head states which weekdays the New Public Market does not open, from a date that has not passed. An empty list restores
/// "open every day" from that date. It is a prospective operating rule: days before <paramref name="EffectiveFrom"/> are never
/// re-read under it, and no collected money is touched.
/// </summary>
/// <param name="Remarks">The authority for the pattern (for example the Head's memorandum). Required when any weekday is closed.</param>
public record SetNpmOperatingScheduleCommand(
    DateOnly EffectiveFrom,
    IReadOnlyList<DayOfWeek> ClosedWeekdays,
    string? Remarks = null) : IRequest<Result<bool>>;
