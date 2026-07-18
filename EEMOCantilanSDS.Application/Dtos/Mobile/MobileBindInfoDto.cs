namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// Resolved from a collector-app bind token (anonymous, pre-login). Tells a freshly installed app which LGU
/// it belongs to (login scope) and carries that LGU's branding so it renders as "their own app".
/// </summary>
public record MobileBindInfoDto(
    string MunicipalityCode,
    string TenantCode,
    string Name,
    string Province,
    string OfficeName,
    string? OfficeAcronym,
    string? SealPath);
