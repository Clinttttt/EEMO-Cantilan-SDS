using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// Checks the figures an office's own sheet states against what the term can possibly support.
///
/// <para>
/// The import collects a <c>Delinquent (₱)</c> and a <c>Whole Year Rental</c> from every row and then discards both.
/// Neither can be stored: whole-year rental is DERIVED (monthly × 12), and there is no opening-balance concept in the
/// domain at all — arrears are computed from the term and the payments recorded against it. Importing a stated arrears
/// figure as an opening balance would add it ON TOP of the arrears the system already calculates for those months, and
/// over-state every such account.
/// </para>
///
/// <para>
/// So the office's figures are used as a CHECK rather than as input. That is the reconciliation the office does by
/// hand, and it is where the N × 12 error showed itself: a three-year term billed 37 months instead of 36 and stated
/// ₱33,300 where the sheet said ₱32,400.
/// </para>
///
/// <para>
/// The check is deliberately one-sided. An import carries no payment history, so the system cannot know what has been
/// collected and cannot judge a figure that is LOWER than the term supports — that is exactly what a part-paid account
/// looks like. It can only be certain when a stated figure EXCEEDS everything the term could ever have billed, which
/// is impossible however much or little was paid. A one-sided check raises no false alarms, and an office that is told
/// the truth every time keeps reading the warnings.
/// </para>
/// </summary>
public static class SheetReconciliation
{
    /// <summary>
    /// The most a monthly-billed term could have billed by <paramref name="asOf"/> — every billing month that has
    /// opened, at the stated rent, with nothing paid.
    ///
    /// <para>The billing months are the N × 12 calendar months beginning with the month of effectivity, matching
    /// <c>Contract.BillsCalendarMonth</c>: a term of N years owes exactly N × 12 months' rent whatever day of the month
    /// it starts. The month in progress is included, because the office's own figure usually does and the bound must be
    /// the generous one — a warning has to be unambiguous.</para>
    /// </summary>
    public static decimal MaximumBillableToDate(
        DateOnly effectivity, int durationYears, decimal monthlyRate, DateOnly asOf)
    {
        if (durationYears <= 0 || durationYears == DomainRules.OpenEndedTermYears) return 0m;
        if (monthlyRate <= 0m) return 0m;

        var monthsElapsed = MonthsOpenedBy(effectivity, asOf);
        if (monthsElapsed <= 0) return 0m;

        var termMonths = durationYears * 12;
        return Math.Min(monthsElapsed, termMonths) * monthlyRate;
    }

    /// <summary>
    /// Whether a stated arrears figure is more than the term could ever have billed. True means the sheet and the
    /// system cannot both be right, and the account is worth a clerk's eye before it is saved.
    /// </summary>
    public static bool ArrearsExceedsWhatTermCouldBill(
        decimal statedArrears, DateOnly effectivity, int durationYears, decimal monthlyRate, DateOnly asOf)
    {
        if (statedArrears <= 0m) return false;

        var ceiling = MaximumBillableToDate(effectivity, durationYears, monthlyRate, asOf);
        // A term that bills nothing — open-ended, or no rate on record — gives no ceiling to compare against, so
        // nothing is asserted rather than flagging every such row.
        return ceiling > 0m && statedArrears > ceiling;
    }

    /// <summary>
    /// Whether a stated whole-year rental disagrees with the rent on the same row. The system derives this figure, so a
    /// difference means one of the two numbers on the office's sheet is wrong — the kind of arithmetic slip that is
    /// invisible until the year is totalled.
    /// </summary>
    public static bool WholeYearDisagreesWithMonthly(decimal statedWholeYear, decimal monthlyRate)
    {
        if (statedWholeYear <= 0m || monthlyRate <= 0m) return false;
        return statedWholeYear != monthlyRate * 12;
    }

    /// <summary>Calendar months from the month of effectivity to the month of <paramref name="asOf"/>, inclusive.</summary>
    private static int MonthsOpenedBy(DateOnly effectivity, DateOnly asOf)
    {
        var first = effectivity.Year * 12 + (effectivity.Month - 1);
        var current = asOf.Year * 12 + (asOf.Month - 1);
        return current - first + 1;
    }
}
