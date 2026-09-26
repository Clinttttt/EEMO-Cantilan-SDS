using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>An immutable classified portion of money received, retaining both policy and origin identity.</summary>
public sealed class CollectionLine : BaseEntity, IMunicipalityOwned
{
    private readonly List<CollectionAllocation> _allocations = [];
    public Guid MunicipalityId { get; private set; }
    public Guid CollectionId { get; private set; }
    public Guid RevenueClassificationId { get; private set; }
    public Guid RevenueClassificationPolicyId { get; private set; }
    public decimal Amount { get; private set; }
    public CollectionSourceKind? SourceKind { get; private set; }
    public Guid? SourceId { get; private set; }
    public CollectionSourcePart? SourcePart { get; private set; }
    public string? CalculationSnapshot { get; private set; }
    public IReadOnlyCollection<CollectionAllocation> Allocations => _allocations.AsReadOnly();

    private CollectionLine() { }

    internal static CollectionLine Create(
        Guid municipalityId,
        Guid collectionId,
        Guid revenueClassificationId,
        Guid revenueClassificationPolicyId,
        decimal amount,
        CollectionSourceKind? sourceKind,
        Guid? sourceId,
        CollectionSourcePart? sourcePart,
        string? calculationSnapshot,
        IReadOnlyList<CollectionAllocationDraft>? allocations)
    {
        if (calculationSnapshot?.Length > 16_384)
            throw new ArgumentException("Calculation snapshot is too large.", nameof(calculationSnapshot));
        var line = new CollectionLine
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            CollectionId = collectionId,
            RevenueClassificationId = revenueClassificationId,
            RevenueClassificationPolicyId = revenueClassificationPolicyId,
            Amount = amount,
            SourceKind = sourceKind,
            SourceId = sourceId,
            SourcePart = sourcePart,
            CalculationSnapshot = calculationSnapshot
        };
        if (allocations is { Count: > 0 })
        {
            if (allocations.Sum(x => x.Amount) != amount)
                throw new ArgumentException("Explicit allocations must equal the collection-line amount.", nameof(allocations));
            foreach (var allocation in allocations)
                line._allocations.Add(CollectionAllocation.Create(municipalityId, line.Id, allocation));
        }
        return line;
    }

    internal static void ValidateSource(
        CollectionSourceKind? sourceKind,
        Guid? sourceId,
        CollectionSourcePart? sourcePart)
    {
        if (sourceKind is null)
        {
            if (sourceId is not null || sourcePart is not null)
                throw new ArgumentException("Source kind, source id, and source part must be absent together.");
            return;
        }

        if (!Enum.IsDefined(sourceKind.Value))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (sourceId is null || sourceId == Guid.Empty)
            throw new ArgumentException("A source id is required when a source kind is supplied.", nameof(sourceId));
        if (sourcePart is { } part && !Enum.IsDefined(part))
            throw new ArgumentOutOfRangeException(nameof(sourcePart));

        var valid = sourceKind.Value switch
        {
            CollectionSourceKind.UtilityBill => sourcePart is CollectionSourcePart.Electricity or CollectionSourcePart.Water,
            CollectionSourceKind.DailyCollection => sourcePart is CollectionSourcePart.DailyFee or CollectionSourcePart.FishFee,
            _ => sourcePart is null
        };

        if (!valid)
            throw new ArgumentException("The source part is missing or is not valid for the selected source kind.", nameof(sourcePart));
    }
}
