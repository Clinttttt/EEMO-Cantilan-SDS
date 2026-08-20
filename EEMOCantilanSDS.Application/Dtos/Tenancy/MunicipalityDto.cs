namespace EEMOCantilanSDS.Application.Dtos.Tenancy;

/// <summary>
/// Public, read-only projection of a <c>Municipality</c> for the CARCANMADCARLAN selector
/// (pre-login). Contains only non-sensitive presentation fields — no operational data.
/// </summary>
public record MunicipalityDto(
    string Code,
    string Name,
    string Province,
    string OfficeName,
    string Status,
    bool IsActive,
    bool IsDefault);

/// <summary>
/// Public, read-only branding for a single LGU, resolved by subdomain identifier for **pre-login** theming
/// (office label, seal, name). Contains only non-sensitive presentation fields — no operational data.
/// <c>Status</c>/<c>IsActive</c> let the login page show "coming soon" for an LGU that isn't live yet.
/// </summary>
public record MunicipalityBrandingDto(
    string Code,
    string TenantCode,
    string Name,
    string Province,
    string OfficeName,
    string? SealPath,
    string Status,
    bool IsActive,
    string? OfficeAcronym = null,
    string? Address = null,
    /// <summary>Signatory lines for this LGU's official sheets, as JSON; null = the office's default trio.</summary>
    string? ReportSignatories = null,
    /// <summary>
    /// True for the platform's DEFAULT municipality.
    ///
    /// <para>
    /// Carried as data because two screens were deciding it by comparing <c>Code</c> to the literal "CANTILAN" - a tenant
    /// code used to decide behaviour, which is the pattern this system does not allow. The municipality row already knows,
    /// so it says so here instead.
    /// </para>
    /// </summary>
    bool IsDefault = false);
