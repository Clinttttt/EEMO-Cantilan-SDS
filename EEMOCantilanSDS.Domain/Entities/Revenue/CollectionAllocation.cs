using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Immutable application of one posted line amount to an authoritative operation-owned source obligation.
/// It stores no assessment or outstanding balance of its own.
/// </summary>
public sealed class CollectionAllocation : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectionLineId { get; private set; }
    public CollectionSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public CollectionSourcePart? SourcePart { get; private set; }
    public decimal Amount { get; private set; }
    public string? SourceSnapshot { get; private set; }

    private CollectionAllocation() { }

    internal static CollectionAllocation Create(
        Guid municipalityId,
        Guid collectionLineId,
        CollectionAllocationDraft draft)
    {
        if (municipalityId == Guid.Empty || collectionLineId == Guid.Empty)
            throw new ArgumentException("Tenant and collection line are required.");
        CollectionLine.ValidateSource(draft.SourceKind, draft.SourceId, draft.SourcePart);
        if (draft.Amount <= 0 || draft.Amount > Collection.MaximumMoneyAmount
            || decimal.Round(draft.Amount, 2, MidpointRounding.ToZero) != draft.Amount)
            throw new ArgumentOutOfRangeException(nameof(draft), "Allocation must be a positive cent amount.");
        if (draft.SourceSnapshot?.Length > 16_384)
            throw new ArgumentException("Source snapshot is too large.", nameof(draft));

        return new CollectionAllocation
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            CollectionLineId = collectionLineId,
            SourceKind = draft.SourceKind,
            SourceId = draft.SourceId,
            SourcePart = draft.SourcePart,
            Amount = draft.Amount,
            SourceSnapshot = draft.SourceSnapshot
        };
    }
}

public sealed record CollectionAllocationDraft(
    CollectionSourceKind SourceKind,
    Guid SourceId,
    decimal Amount,
    CollectionSourcePart? SourcePart = null,
    string? SourceSnapshot = null);
