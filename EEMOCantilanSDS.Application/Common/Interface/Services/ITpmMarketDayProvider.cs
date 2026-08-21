namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// The weekday the current office holds its weekly (Tabo-an) market on.
///
/// <para>
/// Asked AS OF a date, because an office may move its market day and the weeks it has already collected were held
/// on the old one. Answering with today's day for every date would make every attendance already recorded fall on
/// a day the system says was not a market day, and would refuse the office a correction to last week's list. An
/// office that has never moved its day is answered from its registry record, and one that has never stated a day
/// at all is answered Friday, so nothing changes for either.
/// </para>
/// </summary>
public interface ITpmMarketDayProvider
{
    /// <summary>The weekday the market was held on, on <paramref name="asOf"/>.</summary>
    Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default);

    /// <summary>
    /// Every date in the month that is a market day, honouring a change that takes effect part-way through it.
    /// A month the office moved its day in has market days on both weekdays, which is what its own collection
    /// sheets for that month will show.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default);
}
