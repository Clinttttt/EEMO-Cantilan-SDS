using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// A stall's daily fee worked out from the monthly rent the clerk typed: ₱900 a month is ₱30 a day.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is the office's own and already documented: a daily-collected space is LET for a monthly rent, and the
/// daily fee is how that rent is collected, thirty installments to a month. <see cref="DomainRules.DailyBilledMonthDays"/>
/// states the figure, and <c>Stall</c> already runs it the other way round to show a monthly equivalent. This runs it back,
/// so a clerk recording a ₱900 stall does not have to divide.
/// </para>
/// <para>
/// WHERE IT MUST NOT REACH, and the reason this is a class with tests rather than one line in a modal. A figure in a custom
/// section stall's daily field becomes that stall's OWN rate, and an own rate outranks its section's for ever: the office
/// would state a section fee and go on collecting the old figure from every stall in it. That fault was found and fixed
/// once already. The form therefore leaves the field BLANK wherever the section carries a stated fee, and a blank field is
/// blank on purpose. So this derives a figure only into a field that already holds one, which is the case where the stall
/// has to carry its own rate whatever happens, and never over a figure the clerk typed themselves.
/// </para>
/// </remarks>
public static class DailyFeeFromMonthlyRent
{
    /// <summary>
    /// The daily fee to put in the field, or null to leave the field exactly as it is.
    /// </summary>
    /// <param name="monthlyRent">The rent the clerk has typed.</param>
    /// <param name="dailyNow">What the daily field holds at this moment.</param>
    /// <param name="dailyOnOpen">What it held when the form opened, which is the platform's own suggestion.</param>
    /// <param name="lastDerived">The figure this method last put there, so its own work is not mistaken for the clerk's.</param>
    public static decimal? DerivedOrNull(decimal monthlyRent, decimal dailyNow, decimal dailyOnOpen, decimal? lastDerived)
    {
        // Blank on purpose: this stall follows its section's stated fee, and a figure here would outrank it for ever.
        if (dailyNow <= 0m) return null;

        // Nothing to work from. The field keeps whatever it has rather than dropping to nought.
        if (monthlyRent <= 0m) return null;

        // The clerk's own figure stands. Only the form's own suggestion, or this method's own last answer, is replaced.
        if (dailyNow != dailyOnOpen && dailyNow != lastDerived) return null;

        return decimal.Round(monthlyRent / DomainRules.DailyBilledMonthDays, 2);
    }
}
