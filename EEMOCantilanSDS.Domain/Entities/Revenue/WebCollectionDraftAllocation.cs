using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Proposed draft allocation. It does not settle or reserve the source obligation.</summary>
public sealed class WebCollectionDraftAllocation : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid DraftLineId { get; private set; }
    public CollectionSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public CollectionSourcePart? SourcePart { get; private set; }
    public decimal Amount { get; private set; }
    public string? SourceSnapshot { get; private set; }

    private WebCollectionDraftAllocation() { }

    public static WebCollectionDraftAllocation Create(
        Guid municipalityId, Guid draftLineId, CollectionSourceKind sourceKind,
        Guid sourceId, CollectionSourcePart? sourcePart, decimal amount,
        string? sourceSnapshot, string createdBy)
    {
        if (municipalityId == Guid.Empty || draftLineId == Guid.Empty)
            throw new ArgumentException("Tenant and draft line are required.");
        CollectionLine.ValidateSource(sourceKind, sourceId, sourcePart);
        if (amount <= 0 || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (sourceSnapshot?.Length > 16_384)
            throw new ArgumentException("Source snapshot is too large.", nameof(sourceSnapshot));

        return new WebCollectionDraftAllocation
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, DraftLineId = draftLineId,
            SourceKind = sourceKind, SourceId = sourceId, SourcePart = sourcePart,
            Amount = amount, SourceSnapshot = sourceSnapshot,
            CreatedAt = DateTime.UtcNow, CreatedBy = createdBy
        };
    }

    public void UpdateAmountAndSnapshot(decimal amount, string? sourceSnapshot, string updatedBy)
    {
        if (amount <= 0 || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (sourceSnapshot?.Length > 16_384)
            throw new ArgumentException("Source snapshot is too large.", nameof(sourceSnapshot));
        Amount = amount;
        SourceSnapshot = sourceSnapshot;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
