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
/// <param name="Chargeable">
/// Every day of the month this stall owes, collected or not, earliest first.
///
/// <para>Carried so a caller can say WHICH of the payor's own days a date is. A space let on the 9th has the 9th as its
/// first market day, so the 11th is its third — numbering the lines of an entry form 1, 2, 3 described the form rather
/// than the vendor, and the office reconciles against the vendor.</para>
///
/// <para>Excused days and days the market did not open are not here: nothing is owed for them, so they are not the
/// payor's days to count.</para>
/// </param>
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
    int ClosedOrOutsideTerm,
    IReadOnlyList<DateOnly>? Chargeable = null);
