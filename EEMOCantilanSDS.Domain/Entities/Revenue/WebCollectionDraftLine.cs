using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Financial intent stored under a non-financial Web draft.</summary>
public sealed class WebCollectionDraftLine : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid DraftId { get; private set; }
    public int LineOrder { get; private set; }
    public Guid RevenueClassificationId { get; private set; }
    public Guid RevenueClassificationPolicyId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
    public CollectionSourceKind? SourceKind { get; private set; }
    public Guid? SourceId { get; private set; }
    public CollectionSourcePart? SourcePart { get; private set; }
    public string? CalculationSnapshot { get; private set; }

    private WebCollectionDraftLine() { }

    public static WebCollectionDraftLine Create(
        Guid municipalityId, Guid draftId, int lineOrder,
        Guid revenueClassificationId, Guid revenueClassificationPolicyId,
        decimal amount, string? description,
        CollectionSourceKind? sourceKind, Guid? sourceId, CollectionSourcePart? sourcePart,
        string? calculationSnapshot, string createdBy)
    {
        if (municipalityId == Guid.Empty || draftId == Guid.Empty
            || revenueClassificationId == Guid.Empty || revenueClassificationPolicyId == Guid.Empty)
            throw new ArgumentException("Tenant, draft, classification, and policy are required.");
        if (lineOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(lineOrder));
        if (amount <= 0 || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (description?.Length > 300 || calculationSnapshot?.Length > 16_384)
            throw new ArgumentException("Line description or calculation snapshot exceeds its limit.");
        CollectionLine.ValidateSource(sourceKind, sourceId, sourcePart);

        return new WebCollectionDraftLine
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, DraftId = draftId, LineOrder = lineOrder,
            RevenueClassificationId = revenueClassificationId,
            RevenueClassificationPolicyId = revenueClassificationPolicyId,
            Amount = amount, Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SourceKind = sourceKind, SourceId = sourceId, SourcePart = sourcePart,
            CalculationSnapshot = calculationSnapshot,
            CreatedAt = DateTime.UtcNow, CreatedBy = createdBy
        };
    }

    public void UpdateFinancialTerms(
        Guid classificationId, Guid policyId, decimal amount,
        string calculationSnapshot, string updatedBy, string? description = null)
    {
        if (classificationId == Guid.Empty || policyId == Guid.Empty)
            throw new ArgumentException("Classification and policy are required.");
        if (amount <= 0 || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(calculationSnapshot) || calculationSnapshot.Length > 16_384)
            throw new ArgumentException("A bounded, server-generated calculation snapshot is required.", nameof(calculationSnapshot));
        RevenueClassificationId = classificationId;
        RevenueClassificationPolicyId = policyId;
        Amount = amount;
        CalculationSnapshot = calculationSnapshot;
        if (description is not null)
        {
            if (description.Length > 300)
                throw new ArgumentException("Line description must not exceed 300 characters.", nameof(description));
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
