using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Reversal or adjustment of an explicit original obligation allocation.</summary>
public sealed class CollectionCorrectionAllocation : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CorrectionLineId { get; private set; }
    public Guid OriginalAllocationId { get; private set; }
    public decimal FinancialEffectAmount { get; private set; }

    private CollectionCorrectionAllocation() { }

    internal static CollectionCorrectionAllocation Create(
        Guid municipalityId, Guid correctionLineId, CollectionCorrectionAllocationDraft draft) => new()
    {
        Id = Guid.NewGuid(), MunicipalityId = municipalityId, CorrectionLineId = correctionLineId,
        OriginalAllocationId = draft.OriginalAllocationId, FinancialEffectAmount = draft.FinancialEffectAmount
    };
}
