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

    // ── Per-LGU PayMongo credentials (each LGU settles to its own account) ──────────────────────────────
    // The secret + webhook secret are stored ENCRYPTED at rest (the application layer protects/unprotects);
    // the public key is not secret, so it is stored plain.
    //
    // When these are absent, online payments are SHUT for this LGU - unless it is the default municipality, whose own
    // merchant account the global PayMongo configuration is, so the primary client keeps running unchanged.
    //
    // That exception used to apply to everyone: an absent account fell back to the global config for any tenant, which
    // read as a harmless default and was not one. A freshly activated LGU looked as though online payments worked, and
    // its vendors' money would have settled into the default municipality's account.
    public string? PayMongoSecretKeyEnc { get; private set; }
    public string? PayMongoPublicKey { get; private set; }
    public string? PayMongoWebhookSecretEnc { get; private set; }

    /// <summary>
    /// The PayMongo webhook this LGU's notifications arrive through (<c>hook_…</c>), when StallTrack registered it.
    ///
    /// <para>
    /// An identifier, not a secret, so it is stored plain. Kept so the same webhook can be found and updated instead of a
    /// second one being created every time the office saves its keys - PayMongo would happily hold both, and the office
    /// would have no way of telling which one signs what.
    /// </para>
    /// </summary>
    public string? PayMongoWebhookId { get; private set; }

    /// <summary>
    /// When this connection was last confirmed against PayMongo. Null until it has been.
    ///
    /// <para>
    /// Recorded so the office can see that its account answered at some point, rather than trusting a form it filled in
    /// once. PayMongo disables a webhook after repeated delivery failures, so "it worked when we saved it" is not the same
    /// as "it works now".
    /// </para>
    /// </summary>
    public DateTime? PayMongoLastVerifiedAtUtc { get; private set; }

    /// <summary>True when this LGU has its own PayMongo secret configured (so it settles to its own account).</summary>
    public bool HasOwnPayMongoAccount => !string.IsNullOrWhiteSpace(PayMongoSecretKeyEnc);

    /// <summary>
    /// True when this LGU has its own webhook signing secret, so PayMongo's notifications can be authenticated.
    ///
    /// <para>
    /// Without it, an LGU can still take payments - but nothing can confirm them except the payor returning to the portal
    /// or the office reconciling by hand, because an unsigned notification is refused. It is reported so the office can
    /// see that difference rather than discover it.
    /// </para>
    /// </summary>
    public bool HasPayMongoWebhookSecret => !string.IsNullOrWhiteSpace(PayMongoWebhookSecretEnc);

    /// <summary>
    /// Opaque, LGU-scoped token embedded in the collector-app bind link (…/a/{token}). It binds a freshly
    /// installed generic app to THIS municipality (branding + login scope) — it is NOT a security boundary
    /// (login + LGU-scoped accounts remain the real gate). Rotatable if leaked.
    /// </summary>
    public string? MobileBindToken { get; private set; }

    /// <summary>
    /// The signatory lines this LGU prints at the foot of its official sheets, as JSON:
    /// <c>[{"caption":"Prepared by","name":"Administrative Staff"}, …]</c>. Null means "use the office's
    /// default trio", so an LGU that never touches this keeps exactly the sheets it has today. Presentation
    /// only — no report figure depends on it.
    /// </summary>
    public string? ReportSignatories { get; private set; }

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

    /// <summary>
    /// The weekday the office holds its weekly market on NOW. Its history lives in the office's own market-day
    /// schedule, which is what dates are validated against; this is the current arrangement, for the settings
    /// screen and for an office that has never moved its day.
    /// </summary>
    public void SetTpmMarketDay(DayOfWeek day, string updatedBy)
    {
        TpmMarketDay = day;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void MarkUpcoming()    {
        Status = MunicipalityStatus.Upcoming;
        IsActive = false;
    }

    /// <summary>
    /// Replaces the signatory lines printed on this LGU's official sheets. Null or empty restores the
    /// office's default trio — the office must always be able to get its standard sheet back.
    /// </summary>
    public void SetReportSignatories(string? json, string updatedBy = "System")
    {
        ReportSignatories = string.IsNullOrWhiteSpace(json) ? null : json.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Stores this LGU's own PayMongo credentials (secret + webhook already encrypted; public key plain).
    ///
    /// <para>
    /// <paramref name="webhookSecretEnc"/> is written as given, including null. The CALLER decides whether a blank means
    /// "clear it" or "leave what is there" - it knows whether the account itself changed, and this entity does not.
    /// </para>
    /// </summary>
    public void SetPayMongoCredentials(string secretKeyEnc, string? publicKey, string? webhookSecretEnc, string updatedBy)
    {
        PayMongoSecretKeyEnc = secretKeyEnc;
        PayMongoPublicKey = publicKey;
        PayMongoWebhookSecretEnc = webhookSecretEnc;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Forgets which webhook was registered and when the connection last answered, WITHOUT touching the signing secret.
    ///
    /// <para>
    /// For when the LGU points at a different PayMongo account: the old <c>hook_…</c> belongs to that other account, and a
    /// verification recorded against it says nothing about this one. Reporting either would describe a connection that is
    /// not there.
    /// </para>
    /// </summary>
    public void ForgetPayMongoWebhookRegistration(string updatedBy)
    {
        PayMongoWebhookId = null;
        PayMongoLastVerifiedAtUtc = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Records the webhook StallTrack registered for this LGU and its signing secret, and marks the connection verified.
    ///
    /// <para>
    /// Separate from <see cref="SetPayMongoCredentials"/> because it is a different act: the office supplies the secret
    /// key, and this is what the system found out by asking PayMongo. Keeping them apart means a failed provisioning
    /// attempt cannot quietly discard the key the office just entered.
    /// </para>
    /// </summary>
    public void SetPayMongoWebhook(string? webhookId, string? webhookSecretEnc, DateTime verifiedAtUtc, string updatedBy)
    {
        PayMongoWebhookId = string.IsNullOrWhiteSpace(webhookId) ? null : webhookId.Trim();

        // Only replaces the stored secret when a new one was actually obtained. PayMongo reveals a webhook's secret when
        // it is created; asking about an existing webhook does not necessarily return it, and overwriting a working secret
        // with nothing would silently stop every notification from being believed.
        if (!string.IsNullOrWhiteSpace(webhookSecretEnc))
            PayMongoWebhookSecretEnc = webhookSecretEnc;

        PayMongoLastVerifiedAtUtc = verifiedAtUtc;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Records that this LGU's account answered PayMongo successfully, without changing any credential.</summary>
    public void RecordPayMongoVerified(DateTime verifiedAtUtc, string updatedBy)
    {
        PayMongoLastVerifiedAtUtc = verifiedAtUtc;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Removes this LGU's own PayMongo credentials.
    ///
    /// <para>
    /// Everything goes, including the registered webhook and the verification stamp - they describe an account this LGU no
    /// longer uses, and leaving them would report a connection that is not there. Only the DEFAULT municipality can still
    /// take payments afterwards, because the platform configuration is its own account.
    /// </para>
    /// </summary>
    public void ClearPayMongoCredentials(string updatedBy)
    {
        PayMongoSecretKeyEnc = null;
        PayMongoPublicKey = null;
        PayMongoWebhookSecretEnc = null;
        PayMongoWebhookId = null;
        PayMongoLastVerifiedAtUtc = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Sets (or rotates) this LGU's collector-app bind token.</summary>
    public void SetMobileBindToken(string token, string updatedBy = "System")
    {
        MobileBindToken = token;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Generates an opaque, URL-safe bind token (128-bit, base64url, no padding).</summary>
    public static string GenerateBindToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
