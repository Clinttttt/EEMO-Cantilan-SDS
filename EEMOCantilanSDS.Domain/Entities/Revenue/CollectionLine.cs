using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>An immutable classified portion of money received, retaining both policy and origin identity.</summary>
public sealed class CollectionLine : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectionId { get; private set; }
    public Guid RevenueClassificationId { get; private set; }
    public Guid RevenueClassificationPolicyId { get; private set; }
    public decimal Amount { get; private set; }
    public CollectionSourceKind? SourceKind { get; private set; }
    public Guid? SourceId { get; private set; }
    public CollectionSourcePart? SourcePart { get; private set; }

    private CollectionLine() { }

    internal static CollectionLine Create(
        Guid municipalityId,
        Guid collectionId,
        Guid revenueClassificationId,
        Guid revenueClassificationPolicyId,
        decimal amount,
        CollectionSourceKind? sourceKind,
        Guid? sourceId,
        CollectionSourcePart? sourcePart) => new()
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            CollectionId = collectionId,
            RevenueClassificationId = revenueClassificationId,
            RevenueClassificationPolicyId = revenueClassificationPolicyId,
            Amount = amount,
            SourceKind = sourceKind,
            SourceId = sourceId,
            SourcePart = sourcePart
        };

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
