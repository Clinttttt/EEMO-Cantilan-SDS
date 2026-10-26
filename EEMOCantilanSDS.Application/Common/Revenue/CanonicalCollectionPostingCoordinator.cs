using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Application.Common.Interface.Time;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The single atomic writer for canonical collections. Entry workflows validate their specialized
/// source and policy facts, then provide immutable posting inputs and compatibility projections.
/// </summary>
public sealed class CanonicalCollectionPostingCoordinator(IAppDbContext db, IClock? clock = null)
{
    public async Task<Collection> PostAsync(
        Guid municipalityId, Guid clientOperationId, int intentVersion,
        string normalizedIntent, string origin, string actorId, string actorName,
        string actorRole, DateOnly businessDate, string updatedBy,
        IReadOnlyList<CollectionLineDraft> lines, AccountableDocument? document = null,
        Guid? collectorId = null, Guid? payorId = null, string? payerName = null,
        IReadOnlyList<Action<DateTime>>? sourceProjections = null,
        Action<Guid, DateTime>? beforeCommit = null, CancellationToken ct = default,
        NpmRentMonthPostingPermit? npmMonthPermit = null,
        string? receiptReference = null)
    {
        var monthAllocations = lines.SelectMany(x => x.Allocations ?? [])
            .Where(x => x.SourceKind == CollectionSourceKind.NpmRentMonth).ToArray();
        if (monthAllocations.Length != 0 && (npmMonthPermit is null
            || npmMonthPermit.Tenant != municipalityId || npmMonthPermit.Operation != clientOperationId
            || monthAllocations.Any(x => !npmMonthPermit.Months.Contains(x.SourceId))))
            throw new InvalidOperationException("NPM month allocations require the coordinated specialized settlement workflow.");
        if (monthAllocations.Length != 0 && lines.Any(line => line.Classification.SemanticCode != RevenueClassificationCodes.PermanentStallRent
            || line.Allocations is not { Count: > 0 }
            || (line.SourceKind == CollectionSourceKind.NpmRentMonth
                ? line.SourceId is not { } monthId || !npmMonthPermit!.Months.Contains(monthId)
                    || line.Allocations.Any(x => x.SourceKind != CollectionSourceKind.NpmRentMonth || x.SourceId != monthId)
                : line.SourceKind is not null || line.Allocations.Any(x => x.SourceKind != CollectionSourceKind.DailyCollection
                    || x.SourcePart != CollectionSourcePart.DailyFee))))
            throw new InvalidOperationException("A coordinated rental payment must contain only its verified month and daily rent allocations.");
        if (lines.Any(x => x.SourceKind == CollectionSourceKind.NpmRentMonth)
            && monthAllocations.Length == 0)
            throw new InvalidOperationException("NPM month collections require explicit typed allocations.");
        var recordedAtUtc = clock?.UtcNow ?? DateTime.UtcNow;
        var collection = Collection.Post(businessDate, recordedAtUtc, actorId, actorName,
            actorRole, lines, collectorId: collectorId, payerName: payerName,
            clientOperationId: clientOperationId, payorId: payorId, receiptReference: receiptReference);
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
