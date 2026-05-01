using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Vendors.GetVendorRegistry;

public sealed record GetVendorRegistryQuery(
    int Year,
    int Month
) : IRequest<Result<VendorRegistryDto>>;
