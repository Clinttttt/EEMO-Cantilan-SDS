using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// What the market is behind on, for the collector's "Days still owed" screen.
/// </summary>
/// <remarks>
/// Asked for on its own, when the collector opens that screen, rather than carried on the daily round. The round is loaded at
/// every stall and must stay light; this walks each unsettled month of every payor, so it is fetched once and only when
/// somebody is actually chasing arrears.
/// </remarks>
/// <param name="Year">The month the round is on. Every month before it is stated as a settled figure.</param>
/// <param name="Today">The office's own date, so the screen can tell a day gone by from the day in hand.</param>
public sealed record MobileNpmArrearsDto(
    int Year,
    int Month,
    DateOnly Today,
    decimal TotalOutstanding,
    IReadOnlyList<MobileNpmStallArrearsDto> Payors);

/// <summary>One payor's arrears: the months that closed owing, then the days of this month gone by.</summary>
/// <param name="PastMonths">
/// Closed months that still owe, oldest first, each priced by the office's own settlement of that month.
///
/// <para>NOT a count of days times a daily fee. Where the office lets a month for a rent, a month owes that rent whatever its
/// calendar gave it: a 31-day month at ₱30 owes ₱900, not ₱930, and a 28-day February owes ₱900 too - twenty-eight
/// installments plus the month-end difference. Where the office bills pure days, the same figure is simply the days. The rule
/// says which, and it is asked rather than assumed.</para>
/// </param>
/// <param name="DaysOwedThisMonth">
/// The days of the month in progress this payor still owes, EXCLUDING today.
///
/// <para>Today is the daily round's own business and every screen before this one already states it. A day in hand is not an
/// arrear; listing it here asked the collector to chase what he is standing there to collect.</para>
/// </param>
/// <param name="AmountThisMonth">
/// What those days come to, by the same rule: never more than the month has run up, so a month in progress cannot be quoted
/// past its own obligation.
/// </param>
public sealed record MobileNpmStallArrearsDto(
    Guid StallId,
    string StallNo,
    string PayorName,
    MarketSection? Section,
    string SectionName,
    decimal DailyRate,
    IReadOnlyList<MobileNpmMonthArrearDto> PastMonths,
    IReadOnlyList<DateOnly> DaysOwedThisMonth,
    decimal AmountThisMonth)
{
    /// <summary>Everything this payor is behind on, past months and the days gone by this month together.</summary>
    public decimal TotalOutstanding => PastMonths.Sum(m => m.Amount) + AmountThisMonth;
}

/// <summary>
/// A closed month that still owes, as the office settles it.
/// </summary>
/// <param name="Days">
/// The installments the amount is made of. Stated so the collector can see a month's figure is a day's fee counted out rather
/// than a number handed down - and it can be fewer than the month's days, because the last installments of a month that
/// cannot reach its rent are folded into the month-end difference instead.
/// </param>
/// <param name="Amount">The whole figure owed for the month, month-end difference included.</param>
public sealed record MobileNpmMonthArrearDto(
    int Year,
    int Month,
    int Days,
    decimal Amount);
