using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Classified correction effect tied to one original CollectionLine.</summary>
public sealed class CollectionCorrectionLine : BaseEntity, IMunicipalityOwned
{
    private readonly List<CollectionCorrectionAllocation> _allocations = [];

    public Guid MunicipalityId { get; private set; }
    public Guid CorrectionId { get; private set; }
    public Guid OriginalCollectionLineId { get; private set; }
    public decimal FinancialEffectAmount { get; private set; }
    public IReadOnlyCollection<CollectionCorrectionAllocation> Allocations => _allocations.AsReadOnly();

    private CollectionCorrectionLine() { }

    internal static CollectionCorrectionLine Create(
        Guid municipalityId, Guid correctionId, CollectionCorrectionLineDraft draft)
    {
        if (municipalityId == Guid.Empty || correctionId == Guid.Empty || draft.OriginalCollectionLineId == Guid.Empty)
            throw new ArgumentException("Tenant, correction and original line are required.");
        if (draft.FinancialEffectAmount == 0m
            || Math.Abs(draft.FinancialEffectAmount) > Collection.MaximumMoneyAmount
            || decimal.Round(draft.FinancialEffectAmount, 2, MidpointRounding.ToZero) != draft.FinancialEffectAmount)
            throw new ArgumentOutOfRangeException(nameof(draft), "Correction-line effect must be a non-zero cent amount.");

        var allocations = draft.Allocations ?? [];
        if (allocations.Any(x => x.OriginalAllocationId == Guid.Empty
            || x.FinancialEffectAmount == 0m
            || Math.Sign(x.FinancialEffectAmount) != Math.Sign(draft.FinancialEffectAmount)))
            throw new ArgumentException("Correction allocations must reference originals and follow the line-effect sign.", nameof(draft));
        if (allocations.Count > 0 && allocations.Sum(x => x.FinancialEffectAmount) != draft.FinancialEffectAmount)
            throw new ArgumentException("Correction allocation effects must equal the line effect.", nameof(draft));

        var line = new CollectionCorrectionLine
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, CorrectionId = correctionId,
            OriginalCollectionLineId = draft.OriginalCollectionLineId,
            FinancialEffectAmount = draft.FinancialEffectAmount
        };
        foreach (var allocation in allocations)
            line._allocations.Add(CollectionCorrectionAllocation.Create(
                municipalityId, line.Id, allocation));
        return line;
    }
}
