using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IVendorsApiClient
{
    Task<Result<VendorRegistryDto>> GetVendorRegistryAsync(int year, int month);
}
