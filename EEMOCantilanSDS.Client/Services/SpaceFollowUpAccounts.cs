using EEMOCantilanSDS.Application.Dtos.Reports;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// One space account's follow-up under one reason (Delinquent or Arrears), built from the server's per-period space feed. The current
/// term's months and an earlier renewed term's months are summed apart, never into one figure, and every period is counted once.
/// </summary>
public sealed record SpaceFollowUpAccount(
    ObligationFollowUpItemDto First,
    string ReasonKind,
    string? ArrearsBasis,
    int CurrentMonths,
    decimal CurrentOutstanding,
    int EarlierMonths,
    decimal EarlierOutstanding,
    DateOnly OldestPeriod,
    DateOnly LatestPeriod)
{
    public decimal Outstanding => CurrentOutstanding + EarlierOutstanding;
    public int Months => CurrentMonths + EarlierMonths;
    public string Name => string.IsNullOrWhiteSpace(First.PayerName) ? "—" : First.PayerName;

    /// <summary>"Arrears · expired contract (term ended Oct 24, 2025)", or "Delinquent".</summary>
    public string ReasonText => ArrearsBasis switch
    {
        null => "Delinquent",
        "Expired contract" when First.TermLastDay is { } ended && EarlierMonths == 0 => $"Arrears · term ended {ended:MMM d, yyyy}",
        var basis => $"Arrears · {basis.ToLowerInvariant()}"
    };

    public string PeriodText => OldestPeriod == LatestPeriod ? OldestPeriod.ToString("MMM yyyy") : $"{OldestPeriod:MMM yyyy} – {LatestPeriod:MMM yyyy}";

    /// <summary>Groups the feed per account and reason, oldest debt first.</summary>
    public static IReadOnlyList<SpaceFollowUpAccount> Group(IEnumerable<ObligationFollowUpItemDto> items) => items
        .GroupBy(i => (i.AccountId, i.ReasonKind))
        .Select(g =>
        {
            var ordered = g.OrderBy(i => i.PeriodStart).ToList();
            var current = ordered.Where(i => !i.EarlierTerm).ToList();
            var earlier = ordered.Where(i => i.EarlierTerm).ToList();
            // One basis per row: the most telling one present (an ended occupancy, then an expired contract, then a previous year).
            var basis = ordered.Select(i => i.ArrearsBasis).Where(b => b is not null)
                .OrderBy(b => b switch { "Ended occupancy" => 0, "Expired contract" => 1, _ => 2 }).FirstOrDefault();
            return new SpaceFollowUpAccount(ordered[0], g.Key.ReasonKind, basis, current.Count, current.Sum(i => i.Outstanding),
                earlier.Count, earlier.Sum(i => i.Outstanding), ordered[0].PeriodStart, ordered[^1].PeriodStart);
        })
        .OrderBy(a => a.OldestPeriod).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool Matches(string search) => string.IsNullOrWhiteSpace(search)
        || Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || First.SpaceNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || First.Scope.DisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
}
