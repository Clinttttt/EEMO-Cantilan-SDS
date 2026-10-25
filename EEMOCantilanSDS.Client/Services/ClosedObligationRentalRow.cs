using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// A read-only source-owned account, never a manufactured closed Stall. <see cref="Term"/> narrows the row to one contract term of a renewed
/// space: an earlier term (stated as Renewed, with that term's own figures) or the term in force when it has ended. Null reads the whole account.
/// </summary>
public sealed record ClosedObligationRentalRow(string Code, string Unit, SpaceAccountPresentation Source, ObligationAccountDto Account, SpaceTermDto? Term = null)
{
    // A monthly space opens its own profile, where the lifecycle panel lives; an event lot has no lifecycle and keeps its workspace view.
    public string ProfileHref => Code == "FA" ? $"/vendors?workspace={Uri.EscapeDataString(Code)}&account={Account.Id}" : Source.ProfileHref(Account.Id);

    /// <summary>An earlier term of a space that was renewed: the same holder carries on under a later term.</summary>
    public bool IsRenewedTerm => Term is { Current: false };

    public DateOnly Start => Term?.Commencement ?? Account.ActiveFrom;
    /// <summary>When the row stopped being current: the day before the renewal, the closure date, or the last day of a term that ran out.</summary>
    public DateOnly End => Term is { RenewedOn: { } renewed } ? renewed : Account.ActiveTo ?? RentalAccountStatus.TermLastDay(Account) ?? Account.ActiveFrom;
    public decimal Assessed => Term?.Assessed ?? Account.AssessedToDate;
    public decimal Collected => Term?.Collected ?? Account.CollectedToDate;
    public decimal Outstanding => Term?.Outstanding ?? Account.OutstandingToDate;

    /// <summary>
    /// The rows a space account contributes to the register: one per earlier (renewed) term, plus the account itself once it has ended — read
    /// as its term in force only when it was renewed, so no period is stated twice.
    /// </summary>
    public static IEnumerable<ClosedObligationRentalRow> For(string code, string unit, SpaceAccountPresentation source, ObligationAccountDto account, bool ended)
    {
        foreach (var term in account.Terms?.Where(t => !t.Current) ?? [])
            yield return new(code, unit, source, account, term);
        if (ended) yield return new(code, unit, source, account, account.Terms?.FirstOrDefault(t => t.Current));
    }
}
