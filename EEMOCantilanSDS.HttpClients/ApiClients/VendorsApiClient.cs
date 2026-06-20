using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class VendorsApiClient(HttpClient http) : HandleResponse(http), IVendorsApiClient
{
    public async Task<Result<VendorRegistryDto>> GetVendorRegistryAsync(int year, int month)
        => await GetAsync<VendorRegistryDto>($"/api/vendors/registry?year={year}&month={month}");
}
