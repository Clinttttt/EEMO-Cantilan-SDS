using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.HttpClients;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class MobileApiClient(HttpClient http) : HandleResponse(http), IMobileApiClient
{
    public async Task<Result<MobileMenuDto>> GetMenuAsync() =>
        await GetAsync<MobileMenuDto>("api/Mobile/menu");
}
