using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.Mobile.Records;

/// <summary>
/// Display-only projection of the local offline queue. A completed, reference-backed collection
/// session is already represented by the server-owned SRC register. Hide that duplicate card
/// without deleting the local receipt/intent or suppressing failed and unconfirmed operations.
/// </summary>
public static class OfflineCollectionHistory
{
    public static bool IsConfirmedServerSession(PendingOperation operation) =>
        operation.Kind == OfflineOperationKind.ItemizedCollectionSession
        && operation.LocalStatus == PendingLocalStatus.Synced
        && operation.AccountableDocumentId is null
        && string.IsNullOrWhiteSpace(operation.DocumentNumber)
        && string.IsNullOrWhiteSpace(operation.ORNumber)
        && operation.CollectionSessionResult is { Status: CollectionSessionStatus.Recorded, Collections.Count: > 0 } result
        && result.Collections.All(c => c.CollectionId != Guid.Empty && !string.IsNullOrWhiteSpace(c.ReferenceCode));

    public static IReadOnlyList<PendingOperation> Display(IReadOnlyList<PendingOperation> operations) =>
        operations.Where(op => !IsConfirmedServerSession(op)).ToList();

    public static int IssuedDocumentCount(IEnumerable<PendingOperation> operations) =>
        operations.Count(op => op.AccountableDocumentId is not null
            || !string.IsNullOrWhiteSpace(op.DocumentNumber)
            || !string.IsNullOrWhiteSpace(op.ORNumber));

    public static string Title(PendingOperation op) =>
        op.Kind == OfflineOperationKind.ItemizedCollectionSession && string.Equals(op.Title, "Itemized collection", StringComparison.Ordinal)
            ? "Collection checkout"
            : string.IsNullOrWhiteSpace(op.Title) ? "Collection" : op.Title;

    public static decimal? DisplayAmount(PendingOperation op) =>
        op.CollectionSessionResult is { Status: CollectionSessionStatus.Recorded } result
            ? result.GrandTotal
            : op.ReceivedAmount is > 0m ? op.ReceivedAmount
            : op.Amount is > 0m ? op.Amount
            : null;

    public static string? ServerReferences(PendingOperation op)
    {
        if (!string.IsNullOrWhiteSpace(op.ReferenceCode)) return op.ReferenceCode;
        var references = op.CollectionSessionResult?.Collections
            .Select(c => c.ReferenceCode).Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal).ToArray();
        return references is { Length: > 0 } ? string.Join(", ", references) : null;
    }
}
