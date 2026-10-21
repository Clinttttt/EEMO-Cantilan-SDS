namespace EEMOCantilanSDS.Domain.Common.Billing;

/// <summary>Where one unpaid rental obligation (one stall, one occupancy, one billing month) stands. Classified per obligation, never per account.</summary>
public enum ReceivableCategory
{
    /// <summary>The month is still running (or has not started): unpaid, but not yet owed late. Never delinquent.</summary>
    CurrentPeriod = 1,

    /// <summary>A fully elapsed unpaid month of the current calendar year under a live occupancy and an unexpired contract (IA-019, IA-069).</summary>
    Delinquent = 2,

    /// <summary>Unpaid rent from a previous calendar year, or from an ended occupancy or expired contract in any year (IA-020, IA-069).</summary>
    Arrears = 3
}

/// <summary>
/// The one authoritative rule that sorts an unpaid rental obligation into Arrears, Delinquent or current-period (IA-019, IA-020, IA-069).
/// It reads facts about the obligation only - its original billing month, the reference date and whether its occupancy or contract has ended - so
/// the Financial Reports, the Follow-up Queue, the Whole-time History and the Arrears workspace cannot classify differently. It decides a
/// LABEL for read-side reporting; it posts nothing, moves no cash and changes no official revenue line.
/// </summary>
public static class ReceivableClassification
{
    /// <summary>
    /// Sorts one unpaid obligation. <paramref name="reference"/> is the business date the report is read as of (today for a live view).
    /// <paramref name="occupancyEnded"/> is true for an obligation of a former occupancy, or of a contract whose term has run out.
    /// </summary>
    public static ReceivableCategory Classify(int billingYear, int billingMonth, DateOnly reference, bool occupancyEnded)
    {
        // Debt of a former occupancy or an expired contract is Arrears whatever its year: it stays with the original liable occupant and period.
        if (occupancyEnded) return ReceivableCategory.Arrears;

        // The original billing year decides, not the payment date: unpaid December 2025 is Arrears for all of 2026, even for a tenant who stays.
        if (billingYear < reference.Year) return ReceivableCategory.Arrears;

        // A month is delinquent only once it has fully elapsed; the month in progress (and any later one) is current-period unpaid.
        var monthEnd = new DateOnly(billingYear, billingMonth, DateTime.DaysInMonth(billingYear, billingMonth));
        return monthEnd < reference ? ReceivableCategory.Delinquent : ReceivableCategory.CurrentPeriod;
    }
}
