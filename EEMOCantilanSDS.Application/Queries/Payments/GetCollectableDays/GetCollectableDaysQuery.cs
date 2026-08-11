using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetCollectableDays;

/// <summary>
/// The market days of one month that a stall still owes, and the ones it does not.
///
/// <para>
/// Asked by the collection-history import so the days it offers are the payor's OWN uncollected days rather than the
/// first days of the calendar. A vendor whose space was let on the 9th owes nothing for the 1st, and a day already
/// collected cannot be collected again — offering either invites the office to record something that is not true.
/// </para>
///
/// <para>The rules are the ones the import itself applies, so what this offers and what that accepts cannot drift:
/// never a future day, never a day no term answers for, never a facility-wide closure, never a day already collected
/// or excused.</para>
/// </summary>
public record GetCollectableDaysQuery(Guid StallId, int Year, int Month)
    : IRequest<Result<CollectableDaysDto>>;

/// <summary>
/// What a month holds for one stall.
/// </summary>
/// <param name="Uncollected">The days still owed, earliest first. Empty when the month is settled.</param>
/// <param name="AlreadyCollected">Days of this month already collected, so the office can see why there are few left.</param>
/// <param name="Excused">Days the office excused: owed nothing, and not collectable later.</param>
/// <param name="ClosedOrOutsideTerm">Days the market did not open, or that fell outside every term of this stall.</param>
public record CollectableDaysDto(
    Guid StallId,
    int Year,
    int Month,
    IReadOnlyList<DateOnly> Uncollected,
    int AlreadyCollected,
    int Excused,
    int ClosedOrOutsideTerm);
