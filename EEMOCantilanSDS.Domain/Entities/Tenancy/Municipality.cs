using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Tenancy;

/// <summary>
/// An LGU in the CARCANMADCARLAN cluster. This registry is the single source of truth for a
/// municipality's identity, official branding, and rollout status. Cantilan is seeded as the
/// default, active implementation; the others start as Upcoming until onboarded.
///
/// NOTE: this is a standalone reference table. Tenant-scoping of operational data (adding
/// MunicipalityId to Stall/Payment/etc. with EF global query filters) is a later phase and is
/// intentionally NOT part of this entity yet.
/// </summary>
public class Municipality : AuditableEntity
{
    /// <summary>Stable machine code, e.g. CANTILAN, CARRASCAL. Unique, upper-cased.</summary>
    public string Code { get; private set; } = string.Empty;
    /// <summary>
    /// Stable, per-LGU cache/tenant namespace carried in the JWT <c>municipality</c> claim and used by
    /// <c>ITenantContext.TenantCode</c> to isolate each municipality's cache. Cantilan is "cantilan-sds"
    /// (equal to <c>TenantConstants.DefaultTenantCode</c>) so its behaviour is unchanged; every other LGU
    /// gets a distinct code so a second tenant cannot collide with Cantilan's namespace. Unique.
    /// </summary>
    public string TenantCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Province { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    /// <summary>Path to the municipal seal/logo asset (branding).</summary>
    public string? SealPath { get; private set; }
    /// <summary>Revenue office label, e.g. "Economic Enterprise and Management Office (EEMO)".</summary>
    public string OfficeName { get; private set; } = string.Empty;
    public MunicipalityStatus Status { get; private set; }
    /// <summary>The default LGU when no tenant is resolved (Cantilan today).</summary>
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Municipality() { }

    public static Municipality Create(
        string code,
        string name,
        string province,
        MunicipalityStatus status,
        string tenantCode = "",
        string officeName = "",
        string? address = null,
        string? sealPath = null,
        bool isDefault = false,
        string createdBy = "System")
    {
        return new Municipality
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            TenantCode = tenantCode,
            Name = name,
            Province = province,
            OfficeName = officeName,
            Address = address,
            SealPath = sealPath,
            Status = status,
            IsDefault = isDefault,
            IsActive = status == MunicipalityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void Activate()
    {
        Status = MunicipalityStatus.Active;
        IsActive = true;
    }

    public void MarkUpcoming()
    {
        Status = MunicipalityStatus.Upcoming;
        IsActive = false;
    }
}
