using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Tenant-owned business identity. This is independent of portal credentials and owns no assessment,
/// receivable, balance, or settlement state.
/// </summary>
public sealed class Payor : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public BusinessPayorKind Kind { get; private set; }

    private Payor() { }

    public static Payor Create(Guid municipalityId, string displayName, BusinessPayorKind kind, string createdBy)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("Municipality is required.", nameof(municipalityId));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
            throw new ArgumentException("Payor name is required and must not exceed 200 characters.", nameof(displayName));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        return new Payor
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            DisplayName = displayName.Trim(),
            Kind = kind,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void Rename(string displayName, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
            throw new ArgumentException("Payor name is required and must not exceed 200 characters.", nameof(displayName));
        DisplayName = displayName.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
