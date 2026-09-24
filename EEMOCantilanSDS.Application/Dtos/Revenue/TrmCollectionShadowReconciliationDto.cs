using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record TrmCollectionShadowReconciliationDto(
    DateOnly From,
    DateOnly To,
    int SourceCount,
    decimal SourceTotal,
    IReadOnlyList<TrmCollectionShadowProjectedRowDto> ProjectedRows,
    IReadOnlyList<TrmCollectionShadowUnresolvedRowDto> UnresolvedRows)
{
    public int ProjectedCount => ProjectedRows.Count;
    public decimal ProjectedTotal => ProjectedRows.Sum(x => x.Amount);
    public int UnresolvedCount => UnresolvedRows.Count;
    public decimal UnresolvedTotal => UnresolvedRows.Sum(x => x.Amount);

    /// <summary>Authoritative TRM money not represented by a projected classified row.</summary>
    public decimal Difference => SourceTotal - ProjectedTotal;

    public bool IsReconciled =>
        SourceCount == ProjectedCount
        && UnresolvedCount == 0
        && Difference == 0m;
}

public sealed record TrmCollectionShadowProjectedRowDto(
    Guid TrmTripId,
    DateOnly BusinessDate,
    decimal Amount,
    Guid? CollectorId,
    Guid RevenueClassificationId,
    Guid RevenueClassificationPolicyId,
    DateOnly PolicyEffectiveDate,
    CollectionSourceKind SourceKind,
    Guid SourceId);

public sealed record TrmCollectionShadowUnresolvedRowDto(
    Guid TrmTripId,
    DateOnly BusinessDate,
    decimal Amount,
    string ReasonCode,
    string ReasonMessage);

public static class TrmCollectionShadowUnresolvedReasons
{
    public const string TransportationParkingClassificationMissingCode =
        "TRANSPORTATION_PARKING_CLASSIFICATION_MISSING";
    public const string TransportationParkingClassificationMissingMessage =
        "No Transportation/Parking revenue classification is configured for this municipality.";

    public const string TransportationParkingPolicyNotEffectiveCode =
        "TRANSPORTATION_PARKING_POLICY_NOT_EFFECTIVE";
    public const string TransportationParkingPolicyNotEffectiveMessage =
        "No Transportation/Parking revenue classification policy was effective on the trip business date.";
}
