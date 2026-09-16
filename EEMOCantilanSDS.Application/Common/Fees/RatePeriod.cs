namespace EEMOCantilanSDS.Application.Common.Fees;

/// <summary>
/// Which day's rates a screen showing one month should quote.
/// </summary>
/// <remarks>
/// A rate edit takes effect from the day it is made and is never retroactive, so a screen that asks for the rate as of the
/// FIRST of the month answers with whatever the ordinance said before any edit made during it. An office that raised its
/// hog fee to ₱251 on the 15th was shown ₱250 on the 16th — while recording a transaction that same day charged ₱251,
/// because every recording handler resolves at the date the transaction carries.
///
/// <para>
/// So: today while the month is running, which is the date a transaction recorded now will carry; the month's last day
/// once it has closed, which is the rate in force when it did; and its first day for a month not yet begun. Stated once
/// here because three screens quote a fee this way, and a rule copied three times is a rule that drifts twice.
/// </para>
/// </remarks>
public static class RatePeriod
{
    public static DateOnly AsOf(int year, int month, DateOnly today)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        return today < start ? start
             : today > end ? end
             : today;
    }
}
