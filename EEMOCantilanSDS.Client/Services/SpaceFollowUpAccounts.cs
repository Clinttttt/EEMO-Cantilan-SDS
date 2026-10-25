using EEMOCantilanSDS.Application.Dtos.Reports;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// One space ACCOUNT's follow-up, built from the server's per-period space feed (each period classified there). Grouped by the account's id —
/// never by the payer's name — so two accounts of one person stay two rows. Every figure is a sum of the account's own period rows, each period
/// counted once: the Delinquent and Arrears parts, the current-term and earlier-renewed-term parts, and what was already collected on them.
/// </summary>
public sealed record SpaceFollowUpAccount(
    ObligationFollowUpItemDto First,
    /// <summary>"delinquent" or "arrears" for a reason-specific row; "mixed" for an All-view row carrying both.</summary>
    string ReasonKind,
    string? ArrearsBasis,
    int DelinquentMonths,
    decimal DelinquentOutstanding,
    int ArrearsMonths,
    decimal ArrearsOutstanding,
    int CurrentMonths,
    decimal CurrentOutstanding,
    int EarlierMonths,
    decimal EarlierOutstanding,
    decimal Collected,
    DateOnly OldestPeriod,
    DateOnly LatestPeriod)
{
    public decimal Outstanding => DelinquentOutstanding + ArrearsOutstanding;
    public int Months => DelinquentMonths + ArrearsMonths;
    public string Name => string.IsNullOrWhiteSpace(First.PayerName) ? "—" : First.PayerName;
    public Guid AccountId => First.AccountId;

    /// <summary>"Arrears · term ended Oct 24, 2025", "Delinquent", or both parts with their own amounts on an All-view row.</summary>
    public string ReasonText => ReasonKind switch
    {
        "mixed" => $"{ArrearsLabel} ₱{ArrearsOutstanding:N0} · Delinquent ₱{DelinquentOutstanding:N0}",
        "arrears" => ArrearsLabel,
        _ => "Delinquent"
    };

    private string ArrearsLabel => ArrearsBasis switch
    {
        "Expired contract" when First.TermLastDay is { } ended && EarlierMonths == 0 => $"Arrears · term ended {ended:MMM d, yyyy}",
        { } basis => $"Arrears · {basis.ToLowerInvariant()}",
        null => "Arrears"
    };

    public string PeriodText => OldestPeriod == LatestPeriod ? OldestPeriod.ToString("MMM yyyy") : $"{OldestPeriod:MMM yyyy} – {LatestPeriod:MMM yyyy}";
    public string MonthsText => $"{Months} month{(Months == 1 ? "" : "s")}";
    public string PaymentText => Collected > 0m ? $"Partial · ₱{Collected:N0} paid" : "Unpaid";

    /// <summary>One row per account and reason: a reason filter states only that reason's months and amount.</summary>
    public static IReadOnlyList<SpaceFollowUpAccount> Group(IEnumerable<ObligationFollowUpItemDto> items) =>
        Build(items.GroupBy(i => (i.AccountId, i.ReasonKind)).Select(g => (g.Key.ReasonKind, g.ToList())));

    /// <summary>One row per account for the All view: an account with both Delinquent and Arrears months is one row stating each part.</summary>
    public static IReadOnlyList<SpaceFollowUpAccount> GroupByAccount(IEnumerable<ObligationFollowUpItemDto> items) =>
        Build(items.GroupBy(i => i.AccountId).Select(g =>
        {
            var kinds = g.Select(i => i.ReasonKind).Distinct().ToList();
            return (kinds.Count > 1 ? "mixed" : kinds[0], g.ToList());
        }));

    private static IReadOnlyList<SpaceFollowUpAccount> Build(IEnumerable<(string Kind, List<ObligationFollowUpItemDto> Items)> groups) => groups
        .Select(g =>
        {
            var ordered = g.Items.OrderBy(i => i.PeriodStart).ToList();
            var arrears = ordered.Where(i => i.ReasonKind == "arrears").ToList();
            var delinquent = ordered.Where(i => i.ReasonKind != "arrears").ToList();
            var earlier = ordered.Where(i => i.EarlierTerm).ToList();
            var current = ordered.Where(i => !i.EarlierTerm).ToList();
            // One basis per row: the most telling one present (an ended occupancy, then an expired contract, then a previous year).
            var basis = arrears.Select(i => i.ArrearsBasis).Where(b => b is not null)
                .OrderBy(b => b switch { "Ended occupancy" => 0, "Expired contract" => 1, _ => 2 }).FirstOrDefault();
            return new SpaceFollowUpAccount(ordered[0], g.Kind, basis,
                delinquent.Count, delinquent.Sum(i => i.Outstanding), arrears.Count, arrears.Sum(i => i.Outstanding),
                current.Count, current.Sum(i => i.Outstanding), earlier.Count, earlier.Sum(i => i.Outstanding),
                ordered.Sum(i => i.Collected), ordered[0].PeriodStart, ordered[^1].PeriodStart);
        })
        .OrderBy(a => a.OldestPeriod).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.AccountId).ToList();

    /// <summary>The rows a reason chip shows: whole accounts for All, the reason's own part otherwise.</summary>
    public static IReadOnlyList<SpaceFollowUpAccount> For(IEnumerable<ObligationFollowUpItemDto> items, string reason) =>
        reason == "all" ? GroupByAccount(items) : Group(items.Where(i => i.ReasonKind == reason));

    public bool Matches(string search) => string.IsNullOrWhiteSpace(search)
        || Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || First.SpaceNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || First.Scope.DisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
}
