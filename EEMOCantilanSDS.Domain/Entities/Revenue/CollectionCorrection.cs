using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Immutable correction event linked to an original posted Collection/document.</summary>
public sealed class CollectionCorrection : BaseEntity, IMunicipalityOwned
{
    private readonly List<CollectionCorrectionLine> _lines = [];

    public Guid MunicipalityId { get; private set; }
    public Guid OriginalCollectionId { get; private set; }
    public Guid? OriginalDocumentId { get; private set; }
    public Guid? ReplacementDocumentId { get; private set; }
    public Guid? ReplacementCollectionId { get; private set; }
    public CollectionCorrectionType CorrectionType { get; private set; }
    public DateOnly CorrectionEffectiveDate { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public decimal FinancialEffectAmount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public string ActorName { get; private set; } = string.Empty;
    public IReadOnlyCollection<CollectionCorrectionLine> Lines => _lines.AsReadOnly();

    private CollectionCorrection() { }

    public static CollectionCorrection Record(
        Guid municipalityId,
        Guid originalCollectionId,
        Guid? originalDocumentId,
        Guid? replacementDocumentId,
        Guid? replacementCollectionId,
        CollectionCorrectionType correctionType,
        DateOnly correctionEffectiveDate,
        DateTime recordedAtUtc,
        decimal financialEffectAmount,
        string reason,
        string actorId,
        string actorName,
        IEnumerable<CollectionCorrectionLineDraft> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var lineDrafts = lines.ToList();
        if (municipalityId == Guid.Empty || originalCollectionId == Guid.Empty)
            throw new ArgumentException("Tenant and original Collection are required.");
        if (originalDocumentId == Guid.Empty || replacementDocumentId == Guid.Empty || replacementCollectionId == Guid.Empty)
            throw new ArgumentException("Correction references must be valid when supplied.");
        if (replacementCollectionId == originalCollectionId)
            throw new ArgumentException("Replacement Collection must be a distinct event.", nameof(replacementCollectionId));
        if (!Enum.IsDefined(correctionType))
            throw new ArgumentOutOfRangeException(nameof(correctionType));
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Correction recorded time must be UTC.", nameof(recordedAtUtc));
        if (Math.Abs(financialEffectAmount) > Collection.MaximumMoneyAmount
            || decimal.Round(financialEffectAmount, 2, MidpointRounding.ToZero) != financialEffectAmount)
            throw new ArgumentOutOfRangeException(nameof(financialEffectAmount));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("Correction reason is required and must not exceed 500 characters.", nameof(reason));
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Trim().Length > 100
            || string.IsNullOrWhiteSpace(actorName) || actorName.Trim().Length > 150)
            throw new ArgumentException("Correction actor evidence is required.");

        if (correctionType == CollectionCorrectionType.DocumentCorrection
            && (financialEffectAmount != 0m || !replacementDocumentId.HasValue
                || lineDrafts.Count != 0 || replacementCollectionId.HasValue))
            throw new ArgumentException("A document-only correction must link a replacement document and create no financial effect.");
        if (correctionType == CollectionCorrectionType.Replacement
            && (!replacementDocumentId.HasValue || financialEffectAmount > 0m))
            throw new ArgumentException("A replacement must link a replacement document and cannot add a positive financial effect.");
        if (correctionType == CollectionCorrectionType.Reversal && financialEffectAmount >= 0m)
            throw new ArgumentException("A reversal must have an explicit negative financial effect.", nameof(financialEffectAmount));
        if (correctionType == CollectionCorrectionType.Void && financialEffectAmount > 0m)
            throw new ArgumentException("A void cannot add a positive financial effect.", nameof(financialEffectAmount));
        if (financialEffectAmount == 0m && lineDrafts.Count != 0)
            throw new ArgumentException("A zero-effect document correction cannot contain financial-effect lines.", nameof(lines));
        if (financialEffectAmount < 0m && lineDrafts.Any(x => x.FinancialEffectAmount >= 0m))
            throw new ArgumentException("Negative correction events require every financial line effect to be negative.", nameof(lines));
        if (financialEffectAmount > 0m && lineDrafts.Any(x => x.FinancialEffectAmount <= 0m))
            throw new ArgumentException("Positive correction events require every financial line effect to be positive.", nameof(lines));
        if (correctionType == CollectionCorrectionType.DocumentCorrection && !originalDocumentId.HasValue)
            throw new ArgumentException("Document correction requires the original physical document.", nameof(originalDocumentId));

        var totalLineEffect = lineDrafts.Sum(x => x.FinancialEffectAmount);
        if (totalLineEffect != financialEffectAmount)
            throw new ArgumentException("Correction-line effects must equal the correction's financial effect.", nameof(lines));

        var correction = new CollectionCorrection
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, OriginalCollectionId = originalCollectionId,
            OriginalDocumentId = originalDocumentId, ReplacementDocumentId = replacementDocumentId,
            ReplacementCollectionId = replacementCollectionId, CorrectionType = correctionType,
            CorrectionEffectiveDate = correctionEffectiveDate, RecordedAtUtc = recordedAtUtc,
            FinancialEffectAmount = financialEffectAmount, Reason = reason.Trim(),
            ActorId = actorId.Trim(), ActorName = actorName.Trim()
        };

        foreach (var draft in lineDrafts)
            correction._lines.Add(CollectionCorrectionLine.Create(municipalityId, correction.Id, draft));
        return correction;
    }
}

public sealed record CollectionCorrectionLineDraft(
    Guid OriginalCollectionLineId,
    decimal FinancialEffectAmount,
    IReadOnlyList<CollectionCorrectionAllocationDraft>? Allocations = null);

public sealed record CollectionCorrectionAllocationDraft(Guid OriginalAllocationId, decimal FinancialEffectAmount);
