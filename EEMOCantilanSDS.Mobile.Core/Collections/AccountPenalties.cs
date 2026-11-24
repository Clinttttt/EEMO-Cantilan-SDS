using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Collections;

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

    /// <summary>One line per penalty name (Surcharge, Interest, …) in the order the server listed them, summed across months.</summary>
    public static IReadOnlyList<(string Name, decimal Amount)> Lines(IEnumerable<CollectionSessionSourceChoice> open) =>
        open.GroupBy(c => c.DisplayName).Select(g => (g.Key, g.Sum(c => c.ServerAmount ?? 0m))).ToList();

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
