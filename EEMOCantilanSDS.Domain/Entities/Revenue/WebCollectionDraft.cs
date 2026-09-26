using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// A server-persisted, user-owned collection work item. It is not a posted Collection and has no financial effect.
/// </summary>
public sealed class WebCollectionDraft : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? PayorId { get; private set; }
    public string? PayerNameSnapshot { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public RevenueInstrumentType? InstrumentFamily { get; private set; }
    public Guid? AccountableDocumentId { get; private set; }
    public long Revision { get; private set; } = 1;
    public long? ReviewedRevision { get; private set; }
    public string? ReviewedFingerprint { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public CollectionDraftStatus Status { get; private set; } = CollectionDraftStatus.Draft;
    public Guid? CollectionId { get; private set; }

    private WebCollectionDraft() { }

    public static WebCollectionDraft Create(
        Guid municipalityId,
        Guid ownerUserId,
        DateOnly businessDate,
        Guid? payorId,
        string? payerNameSnapshot,
        RevenueInstrumentType? instrumentFamily,
        Guid? accountableDocumentId,
        string createdBy)
    {
        if (municipalityId == Guid.Empty || ownerUserId == Guid.Empty)
            throw new ArgumentException("Tenant and draft owner are required.");
        ValidateOptionalId(payorId, nameof(payorId));
        ValidateOptionalId(accountableDocumentId, nameof(accountableDocumentId));
        if (payorId.HasValue && string.IsNullOrWhiteSpace(payerNameSnapshot))
            throw new ArgumentException("A linked Payor requires a payer-name snapshot.", nameof(payerNameSnapshot));
        if (payerNameSnapshot?.Length > 200)
            throw new ArgumentException("Payer-name snapshot must not exceed 200 characters.", nameof(payerNameSnapshot));
        if (instrumentFamily is { } instrument && !Enum.IsDefined(instrument))
            throw new ArgumentOutOfRangeException(nameof(instrumentFamily));

        return new WebCollectionDraft
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            OwnerUserId = ownerUserId,
            PayorId = payorId,
            PayerNameSnapshot = string.IsNullOrWhiteSpace(payerNameSnapshot) ? null : payerNameSnapshot.Trim(),
            BusinessDate = businessDate,
            InstrumentFamily = instrumentFamily,
            AccountableDocumentId = accountableDocumentId,
            Revision = 1,
            Status = CollectionDraftStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void AdvanceRevision(long expectedRevision, string updatedBy)
    {
        EnsureDraftAndRevision(expectedRevision);
        Revision = checked(Revision + 1);
        InvalidateReview();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Review(long expectedRevision, string normalizedFinancialFingerprint, Guid reviewerUserId, DateTime reviewedAtUtc)
    {
        EnsureDraftAndRevision(expectedRevision);
        if (normalizedFinancialFingerprint is null)
            throw new ArgumentNullException(nameof(normalizedFinancialFingerprint));
        if (reviewerUserId == Guid.Empty)
            throw new ArgumentException("Reviewer is required.", nameof(reviewerUserId));
        if (reviewedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Review time must be UTC.", nameof(reviewedAtUtc));
        if (normalizedFinancialFingerprint.Length != 64
            || normalizedFinancialFingerprint.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A SHA-256 financial-content fingerprint is required.", nameof(normalizedFinancialFingerprint));

        ReviewedRevision = Revision;
        ReviewedFingerprint = normalizedFinancialFingerprint.ToUpperInvariant();
        ReviewedAtUtc = reviewedAtUtc;
        ReviewedByUserId = reviewerUserId;
    }

    public void MarkPosted(long expectedRevision, Guid collectionId, string updatedBy)
    {
        EnsureDraftAndRevision(expectedRevision);
        if (!IsReviewedForCurrentRevision)
            throw new InvalidOperationException("The current draft revision must be reviewed before posting.");
        if (collectionId == Guid.Empty)
            throw new ArgumentException("Posted Collection is required.", nameof(collectionId));

        Status = CollectionDraftStatus.Posted;
        CollectionId = collectionId;
        Revision = checked(Revision + 1);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Discard(long expectedRevision, string updatedBy)
    {
        EnsureDraftAndRevision(expectedRevision);
        Status = CollectionDraftStatus.Discarded;
        Revision = checked(Revision + 1);
        InvalidateReview();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public bool IsReviewedForCurrentRevision =>
        Status == CollectionDraftStatus.Draft
        && ReviewedRevision == Revision
        && !string.IsNullOrWhiteSpace(ReviewedFingerprint);

    private void EnsureDraftAndRevision(long expectedRevision)
    {
        if (Status != CollectionDraftStatus.Draft)
            throw new InvalidOperationException("Only an unposted, undiscarded draft can be changed.");
        if (Revision != expectedRevision)
            throw new InvalidOperationException("Draft revision is stale; reload before continuing.");
    }

    private void InvalidateReview()
    {
        ReviewedRevision = null;
        ReviewedFingerprint = null;
        ReviewedAtUtc = null;
        ReviewedByUserId = null;
    }

    private static void ValidateOptionalId(Guid? value, string name)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identifier must be valid when supplied.", name);
    }
}
