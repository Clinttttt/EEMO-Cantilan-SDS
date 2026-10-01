using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum RemittanceStatus
{
    Recorded = 1,
    Voided = 2
}

/// <summary>
/// Money already collected and turned over (IA-052). A remittance is not a revenue event: it creates no Collection and no
/// line, and it never changes what any report counts as income. It covers whole, already-posted Collections attributable to
/// one collector; each Collection is actively covered by at most one remittance. The expected amount is derived from the
/// covered collections and frozen here; the remitted amount is what was turned over; the difference stays visible and is
/// never absorbed by adjusting a collection. It is append-only: a mistake is voided with a reason, which frees its coverage.
/// </summary>
public sealed class CollectionRemittance : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectorId { get; private set; }
    public DateOnly RemittanceDate { get; private set; }
    public DateOnly PeriodFrom { get; private set; }
    public DateOnly PeriodTo { get; private set; }

    /// <summary>The instrument the covered collections were taken on; null when the remittance covers both.</summary>
    public RevenueInstrumentType? Instrument { get; private set; }

    public decimal ExpectedAmount { get; private set; }
    public decimal RemittedAmount { get; private set; }

    /// <summary>Expected less remitted. Positive is a shortfall that needs review; it is never zero-filled or hidden.</summary>
    public decimal DifferenceAmount { get; private set; }

    public string? Reference { get; private set; }
    public string? Remarks { get; private set; }
    public RemittanceStatus Status { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidedBy { get; private set; }

    /// <summary>Durable idempotency identity: a retry with the same id and intent returns this remittance, never a second one.</summary>
    public Guid ClientOperationId { get; private set; }
    public string IntentFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    public string RecordedBy { get; private set; } = string.Empty;
    public string RecordedByActorId { get; private set; } = string.Empty;
    public int CollectionCount { get; private set; }

    private CollectionRemittance() { }

    public bool NeedsReview => Status == RemittanceStatus.Recorded && DifferenceAmount != 0m;

    public static CollectionRemittance Record(
        Guid municipalityId, Guid collectorId, DateOnly remittanceDate, DateOnly periodFrom, DateOnly periodTo,
        RevenueInstrumentType? instrument, decimal expectedAmount, decimal remittedAmount, int collectionCount,
        string? reference, string? remarks, Guid clientOperationId, string intentFingerprint,
        string recordedBy, string recordedByActorId, DateTime recordedAtUtc)
    {
        if (municipalityId == Guid.Empty || collectorId == Guid.Empty)
            throw new ArgumentException("A tenant and a collector are required.");
        if (periodTo < periodFrom)
            throw new ArgumentException("The coverage period cannot end before it begins.");
        if (collectionCount <= 0 || expectedAmount <= 0m)
            throw new ArgumentException("A remittance covers at least one collection with money to account for.");
        if (remittedAmount <= 0m || decimal.Round(remittedAmount, 2) != remittedAmount)
            throw new ArgumentException("The remitted amount must be a positive amount in whole centavos.", nameof(remittedAmount));
        if (remittedAmount > expectedAmount)
            throw new ArgumentException("A remittance cannot exceed what was collected.", nameof(remittedAmount));
        if (clientOperationId == Guid.Empty || string.IsNullOrWhiteSpace(intentFingerprint))
            throw new ArgumentException("A durable operation identity is required.");
        if (string.IsNullOrWhiteSpace(recordedBy) || string.IsNullOrWhiteSpace(recordedByActorId))
            throw new ArgumentException("The recording actor is required.");
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The recording time must be UTC.", nameof(recordedAtUtc));
        var note = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        var remark = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        if (note?.Length > 100 || remark?.Length > 500)
            throw new ArgumentException("The reference or remarks are too long.");
        return new CollectionRemittance
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            CollectorId = collectorId,
            RemittanceDate = remittanceDate,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            Instrument = instrument,
            ExpectedAmount = expectedAmount,
            RemittedAmount = remittedAmount,
            DifferenceAmount = expectedAmount - remittedAmount,
            Reference = note,
            Remarks = remark,
            Status = RemittanceStatus.Recorded,
            ClientOperationId = clientOperationId,
            IntentFingerprint = intentFingerprint,
            RecordedAtUtc = recordedAtUtc,
            RecordedBy = recordedBy.Trim(),
            RecordedByActorId = recordedByActorId.Trim(),
            CollectionCount = collectionCount
        };
    }

    public void Void(string reason, string actor, DateTime voidedAtUtc)
    {
        if (Status == RemittanceStatus.Voided)
            throw new InvalidOperationException("This remittance is already voided.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
            throw new ArgumentException("A reason of up to 300 characters is required.", nameof(reason));
        if (voidedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The void time must be UTC.", nameof(voidedAtUtc));
        Status = RemittanceStatus.Voided;
        VoidReason = reason.Trim();
        VoidedBy = actor;
        VoidedAtUtc = voidedAtUtc;
    }
}

/// <summary>One Collection covered by a remittance, with the net amount it contributed when covered. Active while the remittance stands.</summary>
public sealed class CollectionRemittanceCoverage : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid RemittanceId { get; private set; }
    public Guid CollectionId { get; private set; }
    public decimal CoveredAmount { get; private set; }

    /// <summary>False only after the remittance is voided; the partial unique index allows one active coverage per Collection.</summary>
    public bool IsActive { get; private set; }

    private CollectionRemittanceCoverage() { }

    public static CollectionRemittanceCoverage Cover(Guid municipalityId, Guid remittanceId, Guid collectionId, decimal coveredAmount)
    {
        if (municipalityId == Guid.Empty || remittanceId == Guid.Empty || collectionId == Guid.Empty)
            throw new ArgumentException("A tenant, remittance and collection are required.");
        if (coveredAmount <= 0m)
            throw new ArgumentException("Only a collection with money to account for can be covered.", nameof(coveredAmount));
        return new CollectionRemittanceCoverage
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            RemittanceId = remittanceId,
            CollectionId = collectionId,
            CoveredAmount = coveredAmount,
            IsActive = true
        };
    }

    public void Release() => IsActive = false;
}
