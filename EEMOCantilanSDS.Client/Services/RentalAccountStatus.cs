using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// The Space/lot account's status on the business date, from the one classification (<see cref="RentalAccountStates"/>), so every register, count and
/// profile agrees: an end date that has not passed is still an active account.
/// </summary>
public static class RentalAccountStatus
{
    public static RentalAccountState Of(ObligationAccountDto a)
    {
        DateOnly? termLast = a.Arrangement == OccupancyArrangement.SignedContract && a.ContractEffectiveDate is { } start && a.ContractYears is { } years
            ? DomainRules.TermLastDay(start, years, a.ContractMonths) : null;
        return RentalAccountStates.Classify(a.ActiveTo, a.Retired, false, termLast, PhilippineTime.Today);
    }

    /// <summary>Open: still collectible and manageable as active (Active, Ending, or a term that has run out).</summary>
    public static bool IsOpen(ObligationAccountDto a) => RentalAccountStates.IsOpen(Of(a));

    /// <summary>
    /// Currently let: Active or Ending. A signed term that has run out is still open (collectible, renewable) but no longer a current holder, so,
    /// as for stall rent, it leaves the current registers and holder lists and is read with the closed and ended accounts.
    /// </summary>
    public static bool IsCurrent(ObligationAccountDto a) => Of(a) is RentalAccountState.Active or RentalAccountState.Ending;

    /// <summary>The account's signed term ran out and it has not been closed: still open, no longer current.</summary>
    public static bool IsTermEnded(ObligationAccountDto a) => Of(a) == RentalAccountState.TermExpired;

    /// <summary>The last day of the recorded signed term, if there is one.</summary>
    public static DateOnly? TermLastDay(ObligationAccountDto a) =>
        a.Arrangement == OccupancyArrangement.SignedContract && a.ContractEffectiveDate is { } start && a.ContractYears is { } years
            ? DomainRules.TermLastDay(start, years, a.ContractMonths) : null;

    /// <summary>The register's short status: Active, Ending, Retired or Closed (the term having run out does not close an account).</summary>
    public static string Label(ObligationAccountDto a) => Of(a) switch
    {
        RentalAccountState.Retired => "Retired",
        RentalAccountState.Ending => "Ending",
        RentalAccountState.TermExpired => "Term ended",
        RentalAccountState.Closed or RentalAccountState.Superseded => "Closed",
        _ => "Active"
    };

    /// <summary>The status with its date, for a list line: "Ends May 3, 2026" or "Closed May 3, 2026".</summary>
    public static string LabelWithDate(ObligationAccountDto a) => Of(a) switch
    {
        RentalAccountState.Ending => $"Ends {a.ActiveTo:MMM d, yyyy}",
        RentalAccountState.TermExpired => TermLastDay(a) is { } last ? $"Term ended {last:MMM d, yyyy}" : "Term ended",
        RentalAccountState.Retired => "Retired",
        RentalAccountState.Closed or RentalAccountState.Superseded => $"Closed {a.ActiveTo:MMM d, yyyy}",
        _ => "Active"
    };
}
