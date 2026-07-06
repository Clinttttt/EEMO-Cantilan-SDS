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
    bool IsActive);
