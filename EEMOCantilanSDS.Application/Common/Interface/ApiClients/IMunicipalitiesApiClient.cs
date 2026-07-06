using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IMunicipalitiesApiClient
{
    /// <summary>Branding for the signed-in user's own LGU (office name/acronym, seal), from
    /// GET /api/municipalities/current/branding. Drives the data-driven shell chrome.</summary>
    Task<Result<MunicipalityBrandingDto>> GetCurrentBrandingAsync();
}
