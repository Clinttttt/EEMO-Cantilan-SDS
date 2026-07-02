using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;

public class ClearNpmMarketClosureCommandHandler(
    INpmMarketClosureRepository closureRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<ClearNpmMarketClosureCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ClearNpmMarketClosureCommand request, CancellationToken ct)
    {
        var existing = await closureRepository.GetAsync(request.Date, ct);
        if (existing is not null)
        {
            closureRepository.Remove(existing);
            await unitOfWork.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode,
                FacilityCode.NPM,
                request.Date.Year,
                request.Date.Month,
                ct);
        }
        return Result<bool>.Success(true);
    }
}
