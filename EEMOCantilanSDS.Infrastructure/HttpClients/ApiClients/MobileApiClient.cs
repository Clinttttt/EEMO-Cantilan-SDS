using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.HttpClients;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class MobileApiClient(HttpClient http) : HandleResponse(http), IMobileApiClient
{
    public async Task<Result<MobileMenuDto>> GetMenuAsync() =>
        await GetAsync<MobileMenuDto>("api/Mobile/menu");

    public async Task<Result<MobileNpmCollectionDto>> GetNpmCollectionAsync(int year, int month) =>
        await GetAsync<MobileNpmCollectionDto>($"api/Mobile/npm/collections?year={year}&month={month}");

    public async Task<Result<bool>> RecordNpmCollectionAsync(RecordMobileNpmCollectionRequest request) =>
        await PostAsync<RecordMobileNpmCollectionRequest, bool>("api/Mobile/npm/collections/record", request);
}
