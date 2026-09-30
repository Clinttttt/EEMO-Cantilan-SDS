using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Immutable, evidenced opening position captured after a scoped reconciliation gate. It is balance evidence,
/// not a Collection, revenue event, or independently editable outstanding balance.
/// </summary>
public sealed class CollectionSettlementCutover : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public CollectionSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public CollectionSourcePart? SourcePart { get; private set; }
    public long BoundaryVersion { get; private set; }
    public DateTime CutoverAtUtc { get; private set; }
    public decimal OpeningAssessmentAmount { get; private set; }
    public decimal OpeningLegacySettledAmount { get; private set; }
    public decimal OpeningOutstandingAmount { get; private set; }
    public string ReconciliationEvidence { get; private set; } = string.Empty;
    public Guid ReconciledByUserId { get; private set; }
    public DateTime ReconciledAtUtc { get; private set; }

    private CollectionSettlementCutover() { }

    public static CollectionSettlementCutover Freeze(
        Guid municipalityId,
        CollectionSourceKind sourceKind,
        Guid sourceId,
        CollectionSourcePart? sourcePart,
        long boundaryVersion,
        DateTime cutoverAtUtc,
        decimal openingAssessmentAmount,
        decimal openingLegacySettledAmount,
        decimal openingOutstandingAmount,
        string reconciliationEvidence,
        Guid reconciledByUserId,
        DateTime reconciledAtUtc)
    {
        if (municipalityId == Guid.Empty || sourceId == Guid.Empty || reconciledByUserId == Guid.Empty)
            throw new ArgumentException("Tenant, source, and reconciling actor are required.");
        CollectionLine.ValidateSource(sourceKind, sourceId, sourcePart);
        if (boundaryVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(boundaryVersion));
        if (cutoverAtUtc.Kind != DateTimeKind.Utc || reconciledAtUtc.Kind != DateTimeKind.Utc
            || reconciledAtUtc < cutoverAtUtc)
            throw new ArgumentException("Cutover and reconciliation timestamps must be UTC and ordered.");
        ValidateMoney(openingAssessmentAmount, nameof(openingAssessmentAmount));
        ValidateMoney(openingLegacySettledAmount, nameof(openingLegacySettledAmount));
        ValidateMoney(openingOutstandingAmount, nameof(openingOutstandingAmount));
        if (openingAssessmentAmount != openingLegacySettledAmount + openingOutstandingAmount)
            throw new ArgumentException("Opening assessment must equal evidenced legacy settlement plus outstanding.");
        if (string.IsNullOrWhiteSpace(reconciliationEvidence) || reconciliationEvidence.Length > 65_536)
            throw new ArgumentException("Reconciliation evidence is required and must not exceed 64 KB.", nameof(reconciliationEvidence));

        return new CollectionSettlementCutover
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, SourceKind = sourceKind,
            SourceId = sourceId, SourcePart = sourcePart, BoundaryVersion = boundaryVersion,
            CutoverAtUtc = cutoverAtUtc, OpeningAssessmentAmount = openingAssessmentAmount,
            OpeningLegacySettledAmount = openingLegacySettledAmount,
            OpeningOutstandingAmount = openingOutstandingAmount,
            ReconciliationEvidence = reconciliationEvidence,
            ReconciledByUserId = reconciledByUserId, ReconciledAtUtc = reconciledAtUtc
        };
    }

    private static void ValidateMoney(decimal amount, string parameterName)
    {
        if (amount < 0 || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
