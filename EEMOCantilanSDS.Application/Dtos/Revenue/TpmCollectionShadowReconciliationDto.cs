using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record TpmCollectionShadowReconciliationDto(
    DateOnly From,
    DateOnly To,
    int SourceCount,
    decimal SourceTotal,
    IReadOnlyList<TpmCollectionShadowProjectedRowDto> ProjectedRows,
    IReadOnlyList<TpmCollectionShadowUnresolvedRowDto> UnresolvedRows)
{
    public int ProjectedCount => ProjectedRows.Count;
    public decimal ProjectedTotal => ProjectedRows.Sum(x => x.Amount);
    public int UnresolvedCount => UnresolvedRows.Count;
    public decimal UnresolvedTotal => UnresolvedRows.Sum(x => x.Amount);

    /// <summary>Authoritative TPM money not represented by a projected classified row.</summary>
    public decimal Difference => SourceTotal - ProjectedTotal;

    public bool IsReconciled =>
        SourceCount == ProjectedCount
        && UnresolvedCount == 0
        && Difference == 0m;
}

public sealed record TpmCollectionShadowProjectedRowDto(
    Guid TpmAttendanceId,
    DateOnly BusinessDate,
    decimal Amount,
    Guid? CollectorId,
    Guid RevenueClassificationId,
    Guid RevenueClassificationPolicyId,
    DateOnly PolicyEffectiveDate,
    CollectionSourceKind SourceKind,
    Guid SourceId);

public sealed record TpmCollectionShadowUnresolvedRowDto(
    Guid TpmAttendanceId,
    DateOnly BusinessDate,
    decimal Amount,
    string ReasonCode,
    string ReasonMessage);

public static class TpmCollectionShadowUnresolvedReasons
{
    public const string TaboClassificationMissingCode = "TABO_CLASSIFICATION_MISSING";
    public const string TaboClassificationMissingMessage =
        "No TABO revenue classification is configured for this municipality.";

    public const string TaboPolicyNotEffectiveCode = "TABO_POLICY_NOT_EFFECTIVE";
    public const string TaboPolicyNotEffectiveMessage =
        "No TABO revenue classification policy was effective on the attendance business date.";
}
