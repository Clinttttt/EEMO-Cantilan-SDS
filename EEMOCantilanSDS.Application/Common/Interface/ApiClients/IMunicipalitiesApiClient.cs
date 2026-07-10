using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IMunicipalitiesApiClient
{
    /// <summary>Branding for the signed-in user's own LGU (office name/acronym, seal), from
    /// GET /api/municipalities/current/branding. Drives the data-driven shell chrome.</summary>
    Task<Result<MunicipalityBrandingDto>> GetCurrentBrandingAsync();

    /// <summary>Anonymous pre-login branding for an LGU resolved by identifier (its TenantCode or Code),
    /// from GET /api/municipalities/{identifier}/branding. Lets the login page theme itself to the LGU.</summary>
    Task<Result<MunicipalityBrandingDto>> GetBrandingByIdentifierAsync(string identifier);

    /// <summary>Anonymous list of municipalities (the CARCANMADCARLAN selector), from GET /api/municipalities.
    /// Used pre-login (e.g. the mobile collector login) to let the user pick their municipality.</summary>
    Task<Result<IReadOnlyList<MunicipalityDto>>> GetMunicipalitiesAsync();
}
