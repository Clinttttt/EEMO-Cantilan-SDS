using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// Shadow-only comparison of NPM weighing money against the WEIGHT_AND_MEASURE classification. It projects only
/// weighing amounts the source froze server-side (Meat: kilos, rate, effective date and amount). Fish weighing rows keep
/// kilos only — their money was never frozen — so they stay unresolved with quantity evidence and no inferred amount.
/// Daily stall rent and any Fish/Meat vendor fee are outside this comparison. Nothing is written.
/// </summary>
public sealed record NpmWeighingShadowReconciliationDto(
    DateOnly From,
    DateOnly To,
    int FrozenSourceCount,
    decimal FrozenSourceTotal,
    decimal UnresolvedFishKilos,
    IReadOnlyList<NpmWeighingShadowProjectedRowDto> ProjectedRows,
    IReadOnlyList<NpmWeighingShadowUnresolvedRowDto> UnresolvedRows)
{
    public int ProjectedCount => ProjectedRows.Count;
    public decimal ProjectedTotal => ProjectedRows.Sum(x => x.Amount);
    public int UnresolvedCount => UnresolvedRows.Count;

    /// <summary>Frozen weighing money not represented by a projected classified row.</summary>
    public decimal Difference => FrozenSourceTotal - ProjectedTotal;

    public bool IsReconciled => UnresolvedCount == 0 && Difference == 0m;
}

public sealed record NpmWeighingShadowProjectedRowDto(
    Guid DailyCollectionId,
    Guid StallId,
    DateOnly BusinessDate,
    CollectionSourcePart SourcePart,
    decimal Kilos,
    decimal RatePerKilo,
    DateOnly RateEffectiveDate,
    decimal Amount,
    Guid? CollectorId,
    Guid RevenueClassificationId,
    Guid RevenueClassificationPolicyId,
    DateOnly PolicyEffectiveDate,
    RevenueInstrumentType? PolicyInstrument,
    CollectionSourceKind SourceKind,
    Guid SourceId);

/// <summary><see cref="Amount"/> is null when the source never froze money for the part.</summary>
public sealed record NpmWeighingShadowUnresolvedRowDto(
    Guid DailyCollectionId,
    DateOnly BusinessDate,
    CollectionSourcePart SourcePart,
    decimal Kilos,
    decimal? Amount,
    string ReasonCode,
    string ReasonMessage);

public static class NpmWeighingShadowUnresolvedReasons
{
    public const string FishRateNotFrozenCode = "FISH_WEIGHING_RATE_NOT_FROZEN";
    public const string FishRateNotFrozenMessage =
        "Fish weighing recorded kilos only; no rate or amount was frozen with the collection, so no amount is inferred.";

    public const string MeatRateEvidenceMissingCode = "MEAT_WEIGHING_RATE_EVIDENCE_MISSING";
    public const string MeatRateEvidenceMissingMessage =
        "Meat kilos exist without a frozen rate, effective date and positive amount.";

    public const string ClassificationMissingCode = "WEIGHT_AND_MEASURE_CLASSIFICATION_MISSING";
    public const string ClassificationMissingMessage =
        "No WEIGHT_AND_MEASURE revenue classification is configured for this municipality.";

    public const string PolicyNotEffectiveCode = "WEIGHT_AND_MEASURE_POLICY_NOT_EFFECTIVE";
    public const string PolicyNotEffectiveMessage =
        "No WEIGHT_AND_MEASURE classification policy was effective on the collection business date.";
}
