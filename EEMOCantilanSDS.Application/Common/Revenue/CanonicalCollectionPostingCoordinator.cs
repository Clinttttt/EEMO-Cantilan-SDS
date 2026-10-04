using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The single atomic writer for canonical collections. Entry workflows validate their specialized
/// source and policy facts, then provide immutable posting inputs and compatibility projections.
/// </summary>
public sealed class CanonicalCollectionPostingCoordinator(IAppDbContext db)
{
    public async Task<Collection> PostAsync(
        Guid municipalityId, Guid clientOperationId, int intentVersion,
        string normalizedIntent, string origin, string actorId, string actorName,
        string actorRole, DateOnly businessDate, string updatedBy,
        IReadOnlyList<CollectionLineDraft> lines, AccountableDocument? document = null,
        Guid? collectorId = null, Guid? payorId = null, string? payerName = null,
        IReadOnlyList<Action<DateTime>>? sourceProjections = null,
        Action<Guid, DateTime>? beforeCommit = null, CancellationToken ct = default)
    {
        var recordedAtUtc = DateTime.UtcNow;
        var collection = Collection.Post(businessDate, recordedAtUtc, actorId, actorName,
            actorRole, lines, collectorId: collectorId, payerName: payerName,
            clientOperationId: clientOperationId, payorId: payorId);
        document?.Consume(collection.Id, clientOperationId, recordedAtUtc, updatedBy);
        foreach (var project in sourceProjections ?? Array.Empty<Action<DateTime>>())
            project(recordedAtUtc);
        beforeCommit?.Invoke(collection.Id, recordedAtUtc);

        var operation = PostingOperation.Record(municipalityId, clientOperationId,
            intentVersion, normalizedIntent, origin, actorId, PostingOperationStatus.Succeeded,
            null, null, collection.Id, document?.Id, recordedAtUtc);
        db.Collections.Add(collection);
        db.PostingOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        return collection;
    }
}
