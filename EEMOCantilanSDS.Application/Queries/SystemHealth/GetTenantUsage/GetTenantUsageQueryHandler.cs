using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.SystemHealth.GetTenantUsage;

public class GetTenantUsageQueryHandler(ITenantUsageRepository repository)
    : IRequestHandler<GetTenantUsageQuery, Result<TenantUsageDto>>
{
    public async Task<Result<TenantUsageDto>> Handle(GetTenantUsageQuery request, CancellationToken cancellationToken)
    {
        var usage = await repository.GetUsageAsync(cancellationToken);
        return Result<TenantUsageDto>.Success(usage);
    }
}
