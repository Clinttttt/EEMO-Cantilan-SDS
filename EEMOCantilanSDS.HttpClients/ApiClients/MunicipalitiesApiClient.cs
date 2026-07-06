using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class MunicipalitiesApiClient(HttpClient http) : HandleResponse(http), IMunicipalitiesApiClient
{
    public async Task<Result<MunicipalityBrandingDto>> GetCurrentBrandingAsync() =>
        await GetAsync<MunicipalityBrandingDto>("api/municipalities/current/branding");
}
