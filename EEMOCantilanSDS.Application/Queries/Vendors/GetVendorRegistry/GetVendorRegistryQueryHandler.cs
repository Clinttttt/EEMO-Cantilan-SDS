using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Vendors.GetVendorRegistry;

public sealed class GetVendorRegistryQueryHandler(
    IVendorRepository vendorRepository
) : IRequestHandler<GetVendorRegistryQuery, Result<VendorRegistryDto>>
{
    public async Task<Result<VendorRegistryDto>> Handle(
        GetVendorRegistryQuery request,
        CancellationToken cancellationToken)
    {
        var registry = await vendorRepository.GetVendorRegistryAsync(
            request.Year,
            request.Month,
            cancellationToken);

        return Result<VendorRegistryDto>.Success(registry);
    }
}
