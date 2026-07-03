using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetClosedStallAccounts;

public class GetClosedStallAccountsQueryHandler(
    IStallRepository stallRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions)
    : IRequestHandler<GetClosedStallAccountsQuery, Result<IReadOnlyList<ClosedStallAccountDto>>>
{
    public async Task<Result<IReadOnlyList<ClosedStallAccountDto>>> Handle(
        GetClosedStallAccountsQuery request, CancellationToken ct)
    {
        var asOf = PhilippineTime.Today;
        var key = EemoCacheKeys.ClosedAccounts(tenantContext.TenantCode, asOf);
        var regions = EemoCacheRegions.ClosedAccountsRegions(tenantContext.TenantCode);
        var result = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.ClosedAccountsTtl,
            token => stallRepository.GetClosedStallAccountsAsync(token),
            ct);

        return Result<IReadOnlyList<ClosedStallAccountDto>>.Success(result);
    }
}
