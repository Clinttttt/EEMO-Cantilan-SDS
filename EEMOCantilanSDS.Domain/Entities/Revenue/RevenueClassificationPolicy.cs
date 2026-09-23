using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// An immutable, effective-dated tenant policy version for displaying and instrumenting a revenue
/// classification. It intentionally does not represent an issued document or a money calculation.
/// </summary>
public sealed class RevenueClassificationPolicy : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid RevenueClassificationId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>
    /// Null means no instrument has been approved/configured for this classification. In particular,
    /// it does not infer an OR or Cash Ticket policy.
    /// </summary>
    public RevenueInstrumentType? PermittedInstrumentType { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }

    private RevenueClassificationPolicy() { }

    public static RevenueClassificationPolicy Create(
        Guid revenueClassificationId,
        DateOnly effectiveDate,
        string displayName,
        RevenueInstrumentType? permittedInstrumentType,
        Guid municipalityId = default,
        string? description = null,
        string createdBy = "System")
    {
        if (revenueClassificationId == Guid.Empty)
            throw new ArgumentException("A classification is required.", nameof(revenueClassificationId));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160)
            throw new ArgumentException("Display name is required and must not exceed 160 characters.", nameof(displayName));
        if (description?.Length > 500)
            throw new ArgumentException("Description must not exceed 500 characters.", nameof(description));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Length > 100)
            throw new ArgumentException("Created by is required and must not exceed 100 characters.", nameof(createdBy));
        if (permittedInstrumentType is { } instrument && !Enum.IsDefined(instrument))
            throw new ArgumentOutOfRangeException(nameof(permittedInstrumentType));

        return new RevenueClassificationPolicy
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            RevenueClassificationId = revenueClassificationId,
            EffectiveDate = effectiveDate,
            DisplayName = displayName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            PermittedInstrumentType = permittedInstrumentType,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }
}
