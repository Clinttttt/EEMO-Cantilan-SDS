using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;

public class ClearStallMonthlyExceptionCommandHandler(
    IStallMonthlyExceptionRepository exceptionRepository,
    IStallRepository stallRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<ClearStallMonthlyExceptionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ClearStallMonthlyExceptionCommand request, CancellationToken ct)
    {
        var existing = await exceptionRepository.GetAsync(request.StallId, request.Year, request.Month, ct);
        if (existing is not null)
        {
            var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
            exceptionRepository.Remove(existing);
            await unitOfWork.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode,
                stall?.Facility?.Code,
                request.Year,
                request.Month,
                ct);
        }
        return Result<bool>.Success(true);
    }
}
