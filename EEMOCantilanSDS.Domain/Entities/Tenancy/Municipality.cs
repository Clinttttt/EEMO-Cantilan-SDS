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
    /// <summary>Short office acronym for compact UI labels, e.g. "EEMO" / "LEEO". Optional (nullable);
    /// the UI falls back to its default when absent, so Cantilan is unaffected.</summary>
    public string? OfficeAcronym { get; private set; }
    public MunicipalityStatus Status { get; private set; }
    /// <summary>The default LGU when no tenant is resolved (Cantilan today).</summary>
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>The weekday this LGU holds its weekly (Tabo-an) market. Null = Friday (the Cantilan default),
    /// so existing behaviour and Phase-0 goldens are unchanged; other LGUs set their own day at activation.</summary>
    public DayOfWeek? TpmMarketDay { get; private set; }

    // ── Per-LGU PayMongo credentials (Option A: each LGU settles to its own account) ────────────────────
    // The secret + webhook secret are stored ENCRYPTED at rest (the application layer protects/unprotects);
    // the public key is not secret, so it is stored plain. When these are absent, the online-payment gateway
    // falls back to the GLOBAL PayMongo config — which is exactly how the default LGU (Cantilan, the primary
    // client) keeps running byte-for-byte on the primary account.
    public string? PayMongoSecretKeyEnc { get; private set; }
    public string? PayMongoPublicKey { get; private set; }
    public string? PayMongoWebhookSecretEnc { get; private set; }

    /// <summary>True when this LGU has its own PayMongo secret configured (so it settles to its own account).</summary>
    public bool HasOwnPayMongoAccount => !string.IsNullOrWhiteSpace(PayMongoSecretKeyEnc);

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
        string createdBy = "System",
        string? officeAcronym = null,
        DayOfWeek? tpmMarketDay = null)
    {
        return new Municipality
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            TenantCode = tenantCode,
            Name = name,
            Province = province,
            OfficeName = officeName,
            OfficeAcronym = officeAcronym,
            Address = address,
            SealPath = sealPath,
            Status = status,
            IsDefault = isDefault,
            IsActive = status == MunicipalityStatus.Active,
            TpmMarketDay = tpmMarketDay,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void Activate()
    {
        Status = MunicipalityStatus.Active;
        IsActive = true;
    }

    /// <summary>
    /// Applies the onboarding branding (office label, address, seal) captured during onboarding. Used at
    /// activation to stamp the LGU's official identity onto its registry record. Only overwrites a field
    /// when a non-empty value is supplied, so partial profiles never blank existing data.
    /// </summary>
    public void ApplyOnboardingProfile(string? officeName, string? address, string? sealPath, string? officeAcronym = null, string updatedBy = "System", DayOfWeek? tpmMarketDay = null)
    {
        if (!string.IsNullOrWhiteSpace(officeName)) OfficeName = officeName.Trim();
        if (!string.IsNullOrWhiteSpace(address)) Address = address.Trim();
        if (!string.IsNullOrWhiteSpace(sealPath)) SealPath = sealPath.Trim();
        if (!string.IsNullOrWhiteSpace(officeAcronym)) OfficeAcronym = officeAcronym.Trim();
        if (tpmMarketDay is not null) TpmMarketDay = tpmMarketDay;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void MarkUpcoming()
    {
        Status = MunicipalityStatus.Upcoming;
        IsActive = false;
    }

    /// <summary>Stores this LGU's own PayMongo credentials (secret + webhook already encrypted; public key plain).</summary>
    public void SetPayMongoCredentials(string secretKeyEnc, string? publicKey, string? webhookSecretEnc, string updatedBy)
    {
        PayMongoSecretKeyEnc = secretKeyEnc;
        PayMongoPublicKey = publicKey;
        PayMongoWebhookSecretEnc = webhookSecretEnc;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Removes this LGU's own PayMongo credentials, reverting it to the global fallback account.</summary>
    public void ClearPayMongoCredentials(string updatedBy)
    {
        PayMongoSecretKeyEnc = null;
        PayMongoPublicKey = null;
        PayMongoWebhookSecretEnc = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
