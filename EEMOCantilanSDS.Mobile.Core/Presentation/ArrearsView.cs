using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Presentation;

/// <summary>What the collector has chosen to narrow the Arrears and Delinquency list by. Narrowing never changes an amount.</summary>
public sealed record ArrearsFilter(string? Search = null, string Reason = ArrearsView.AllReasons, string? OperationCode = null);

/// <summary>
/// Pure presentation logic for the Collector Mobile Arrears and Delinquency workspace. Every figure is the server's; this only filters, totals what is shown
/// and words a period span. It never decides what is owed or whether a collection can be taken.
/// </summary>
public static class ArrearsView
{
    public const string AllReasons = "all";
    public const string ArrearsOnly = "arrears";
    public const string DelinquentOnly = "delinquent";

    public static IReadOnlyList<ArrearsAccountDto> Apply(IEnumerable<ArrearsAccountDto> accounts, ArrearsFilter filter)
    {
        var term = (filter.Search ?? "").Trim();
        return accounts
            .Where(a => filter.OperationCode is null || a.OperationCode == filter.OperationCode)
            .Where(a => filter.Reason switch { ArrearsOnly => a.ArrearsAmount > 0m, DelinquentOnly => a.DelinquentAmount > 0m, _ => true })
            .Where(a => term.Length == 0 || new[] { a.Payer, a.Space, a.OperationName, a.Section ?? "" }
                .Any(v => v.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>The operations that actually have an account to show, each once, in the order the server first listed them.</summary>
    public static IReadOnlyList<(string Code, string Name)> Operations(IEnumerable<ArrearsAccountDto> accounts) =>
        accounts.GroupBy(a => a.OperationCode).Select(g => (g.Key, g.First().OperationName)).OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Totals of exactly the rows shown. A filtered view states its own sum, so the header never claims more than the list.</summary>
    public static (int Accounts, decimal Total, decimal Arrears, decimal Delinquent) Totals(IReadOnlyCollection<ArrearsAccountDto> shown) =>
        (shown.Count, shown.Sum(a => a.Total), shown.Sum(a => a.ArrearsAmount), shown.Sum(a => a.DelinquentAmount));

    /// <summary>"Apr 2026 to Sep 2026", or one month, or null when the server states no span.</summary>
    public static string? Span(ArrearsAccountDto a)
    {
        static string? Month(string? period) =>
            DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("MMM yyyy", CultureInfo.InvariantCulture) : null;
        var first = Month(a.FirstPeriod);
        var last = Month(a.LastPeriod);
        return first is null ? last : last is null || last == first ? first : $"{first} to {last}";
    }

    public static string ReasonLabel(ArrearsAccountDto a) => a.Reason switch { "both" => "Arrears and delinquent", "arrears" => "Arrears", _ => "Delinquent" };

    public static string MonthsLabel(ArrearsAccountDto a) => a.UnpaidMonths == 1 ? "1 month" : $"{a.UnpaidMonths} months";

    /// <summary>Why a collection cannot be opened from here, or null when it can. Stated plainly, never as a code.</summary>
    public static string? CollectBlockedReason(ArrearsAccountDto a) =>
        a.CanCollect ? null
        : a.CollectVia == "FacilityMonthly" ? "This rent is collected in the facility's monthly collection, not in New Collection."
        : "This account is billed daily. It is collected from its daily stall payments, not here.";

    /// <summary>The facility whose own monthly collection takes this rent (for example "BBQ"), when New Collection cannot; otherwise null.</summary>
    public static string? FacilityMonthlyRoute(ArrearsAccountDto a) =>
        a.CollectVia == "FacilityMonthly" && a.OperationCode.StartsWith("FACILITY_", StringComparison.Ordinal) ? a.OperationCode["FACILITY_".Length..] : null;
}
