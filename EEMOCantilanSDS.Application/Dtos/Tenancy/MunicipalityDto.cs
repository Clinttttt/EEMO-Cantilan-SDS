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
