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

        // WHOLE PESOS, because a collector takes cash at a stall and cannot make change for 67 centavos. Every fee this
        // platform bills daily is a whole peso for the same reason, and an ordinance schedule is written that way.
        //
        // WHAT THE OFFICE IS ACCEPTING, measured rather than reasoned about, because two attempts at reasoning got it
        // wrong. This figure only ever reaches a stall in one of the office's OWN sections, and such a stall is let at its
        // own daily rate: Stall.ResolveMonthlyRent makes its month thirty of those, since the office's stated market month
        // does not apply to a section it does not price. So ₱800 a month derives ₱27 a day, and the month that stall owes
        // is ₱810 - while the monthly figure typed above it, which is the CONTRACT's record, still reads ₱800.
        //
        // The divergence is not caused by rounding: at ₱26.67 the month owed ₱800.10. Rounding widens it from ten centavos
        // to ten pesos, and buys a fee a collector can actually take in cash. Pinned by
        // NpmMonthSettlementServiceTests.ACustomSectionStallsMonthIsThirtyOfItsOwnRoundedDailyFee.
        return decimal.Round(monthlyRent / DomainRules.DailyBilledMonthDays, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The other direction: the monthly rent a daily fee implies, or null to leave the field alone.
    /// </summary>
    /// <remarks>
    /// A stall in one of the office's OWN market sections is let at its own daily rate, and its month IS thirty of those -
    /// <see cref="Domain.Entities.Facilities.Stall.ResolveMonthlyRent"/> says so, ignoring the monthly field entirely for such a
    /// stall. So where the daily rate is known and the monthly is empty, the monthly figure is not a guess: it is the same
    /// arithmetic the platform bills by, read the other way.
    ///
    /// <para>Why it is needed rather than merely convenient: on the rent-goal basis the SERVER refuses a market stall with no
    /// monthly rate (<c>CreateStallCommandValidator.BeStatedWhereAMonthIsARent</c>). The form opened a custom-section stall with
    /// a daily rate of ₱30 and a monthly of nought, so the office filled the form in, pressed Add Vendor, and was refused with
    /// nothing on screen to say what figure was wanted.</para>
    ///
    /// <para>Restrained exactly as the forward direction is: never while EDITING, never over a figure somebody typed, and only
    /// into an EMPTY monthly field - a rent already recorded is the contract's own record and must not move because a daily rate
    /// was corrected.</para>
    /// </remarks>
    /// <param name="dailyFee">The daily fee the stall is let at.</param>
    /// <param name="monthlyNow">What the monthly field holds at this moment.</param>
    /// <param name="lastDerived">The figure this method last put there, so its own work is not mistaken for the clerk's.</param>
    public static decimal? MonthlyFromDailyOrNull(decimal dailyFee, decimal monthlyNow, decimal? lastDerived)
    {
        // Nothing to work from.
        if (dailyFee <= 0m) return null;

        // Only an empty field, or this method's own last answer, is filled. A rent the clerk typed stands.
        if (monthlyNow > 0m && monthlyNow != lastDerived) return null;

        return dailyFee * DomainRules.DailyBilledMonthDays;
    }
}
