using EEMOCantilanSDS.Domain.Common;
using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// A tenant-owned stable semantic identity for a category of revenue. This internal code is not an
/// official accounting code; tenant display wording is kept in effective-dated policy versions.
/// </summary>
public sealed class RevenueClassification : AuditableEntity, IMunicipalityOwned
{
    private static readonly Regex ValidSemanticCode = new("^[A-Z][A-Z0-9_]{1,79}$", RegexOptions.Compiled);

    public Guid MunicipalityId { get; private set; }
    public string SemanticCode { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    private RevenueClassification() { }

    public static RevenueClassification Create(
        string semanticCode,
        Guid municipalityId = default,
        string createdBy = "System")
    {
        if (string.IsNullOrWhiteSpace(semanticCode)
            || !ValidSemanticCode.IsMatch(semanticCode))
            throw new ArgumentException(
                "Semantic code must be 2-80 uppercase letters, digits, or underscores and start with a letter.",
                nameof(semanticCode));

        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Length > 100)
            throw new ArgumentException("Created by is required and must not exceed 100 characters.", nameof(createdBy));

        return new RevenueClassification
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            SemanticCode = semanticCode,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void Retire(string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(updatedBy) || updatedBy.Length > 100)
            throw new ArgumentException("Updated by is required and must not exceed 100 characters.", nameof(updatedBy));

        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Resolves only this classification's policy versions for its owning municipality. The caller should
    /// still obtain the versions through the tenant-filtered persistence context; these identity checks
    /// prevent a mixed collection from resolving another classification's or tenant's policy by mistake.
    /// </summary>
    public RevenueClassificationPolicy? ResolvePolicyAsOf(
        IEnumerable<RevenueClassificationPolicy> versions,
        DateOnly asOf) => versions
            .Where(x => x.MunicipalityId == MunicipalityId
                && x.RevenueClassificationId == Id
                && x.EffectiveDate <= asOf)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefault();
}
