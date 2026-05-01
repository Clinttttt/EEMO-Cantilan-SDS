using EEMOCantilanSDS.Application.Dtos.Vendors;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IVendorRepository
{
    Task<VendorRegistryDto> GetVendorRegistryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);
}
