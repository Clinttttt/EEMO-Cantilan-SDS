using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Collections;

public sealed record PenaltyLine(string Name, decimal Amount, string? Months);

/// <summary>
/// The penalties still open on one rent account (surcharge, interest, assessed fines), collected in full on the same receipt as the rent.
/// The server lists, prices and posts each one through the account's own fine source; this only reads that list and asks for all of it.
/// </summary>
public static class AccountPenalties
{
    /// <summary>The account's open, collectible penalties as the server offered them, each at its remaining amount.</summary>
    public static IReadOnlyList<CollectionSessionSourceChoice> Open(CollectionSessionDiscovery? discovery) =>
        (discovery?.Operations ?? [])
            .Where(o => o.Kind == CollectionSessionItemKind.FineAssessment && o.Supported && o.CanAdd)
            .SelectMany(o => o.Choices ?? [])
            .Where(c => c.Kind == CollectionSessionItemKind.FineAssessment && c.Identity.FineAssessmentId is not null && c.ServerAmount > 0m)
            .ToList();

    /// <summary>
    /// One line per penalty name summed across months, surcharge before interest as the Statement of Account lists them, each with the months it is
    /// for as the server labels them ("October 2026", "January – October 2026 · 10 months"). Display only; the amounts are the server's.
    /// </summary>
    public static IReadOnlyList<PenaltyLine> Lines(IEnumerable<CollectionSessionSourceChoice> open) =>
        open.Select((c, i) => (Choice: c, Index: i)).GroupBy(x => x.Choice.DisplayName)
            .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Min(x => x.Index))
            .Select(g => new PenaltyLine(g.Key, g.Sum(x => x.Choice.ServerAmount ?? 0m), Months(g.Select(x => x.Choice.Context))))
            .ToList();

    private static int Rank(string name) =>
        name.Contains("surcharge", StringComparison.OrdinalIgnoreCase) ? 0 : name.Contains("interest", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    /// <summary>The months a penalty line covers, from the server's "MMMM yyyy" period labels; null when a label is not a month.</summary>
    public static string? Months(IEnumerable<string?> labels)
    {
        var months = new SortedSet<DateTime>();
        foreach (var label in labels)
        {
            if (!DateTime.TryParseExact(label, "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) return null;
            months.Add(month);
        }
        if (months.Count == 0) return null;
        string Name(DateTime m) => m.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        if (months.Count == 1) return Name(months.Min);
        var from = months.Min.Year == months.Max.Year ? months.Min.ToString("MMMM", CultureInfo.InvariantCulture) : Name(months.Min);
        return $"{from} – {Name(months.Max)} · {months.Count} months";
    }

    public static decimal Total(IEnumerable<CollectionSessionSourceChoice> open) => open.Sum(c => c.ServerAmount ?? 0m);

    /// <summary>
    /// One checkout of every open penalty of this account at its full remaining amount, under the receipt number typed for the rent (if any).
    /// Build it once: the same intent is quoted and then queued, so the quote fingerprint matches.
    /// </summary>
    public static CollectionSessionIntent Intent(Guid sessionId, DateOnly businessDate, Guid contractId,
        IEnumerable<CollectionSessionSourceChoice> open, string? receiptNumber) =>
        new(sessionId, businessDate, null,
            open.Select(c => new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.FineAssessment, c.ServerAmount!.Value,
                Fine: new SessionFineAssessmentIntent(c.Identity.FineAssessmentId!.Value, c.Identity.FineVersion))).ToArray(),
            SourceIdentity: new CollectionSourceIdentity(SourceIdentityKind.Occupancy, contractId),
            ReceiptReference: ReceiptReferenceEntry.Number(true, receiptNumber));
}
