using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Why an unpaid space period is Arrears, by the stall rules (IA-019, IA-020, IA-069), or null when it is Delinquent. One rule for every screen
/// that states it — the follow-up feeds and the Collector Mobile choice — so they can never disagree. It reads only; it changes no balance.
/// </summary>
public static class SpaceArrears
{
    public const string EndedOccupancy = "Ended occupancy";
    public const string ExpiredContract = "Expired contract";
    public const string PreviousYear = "Previous year";

    /// <summary>The last day of the term in force: the latest renewal, or the account's own signed term. Null for a space held without a contract.</summary>
    public static DateOnly? TermLastDay(ObligationAccount account, IReadOnlyList<SpaceContractTerm> ordered) =>
        ordered.Count > 0 ? ordered[^1].LastDay
        : account.Arrangement == OccupancyArrangement.SignedContract && account.ContractEffectiveDate is { } start && account.ContractYears is { } years
            ? DomainRules.TermLastDay(start, years) : null;

    /// <summary>An ended occupancy, then an expired contract (a term that has run out, or an earlier renewed term), then a previous calendar year.</summary>
    public static string? Basis(ObligationAccount account, IReadOnlyList<SpaceContractTerm> ordered, DateOnly periodStart, DateOnly asOf)
    {
        if (account.ActiveTo is { } closed && closed < asOf) return EndedOccupancy;
        var earlierTerm = SpaceTermSplit.CurrentTermStart(ordered) is { } termStart && periodStart < termStart;
        if (earlierTerm || TermLastDay(account, ordered) is { } last && last < asOf) return ExpiredContract;
        return periodStart.Year < asOf.Year ? PreviousYear : null;
    }
}
