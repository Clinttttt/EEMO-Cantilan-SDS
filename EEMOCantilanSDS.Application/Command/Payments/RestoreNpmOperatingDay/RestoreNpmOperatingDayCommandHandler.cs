using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RestoreNpmOperatingDay;

public class RestoreNpmOperatingDayCommandHandler(
    INpmMarketClosureRepository closureRepository,
    ICurrentUserService currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext, NpmRentMonthWorkflow? monthly = null) : IRequestHandler<RestoreNpmOperatingDayCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RestoreNpmOperatingDayCommand request, CancellationToken ct)
    {
        if (NpmOperatingDayAuthority.Check(currentUser, request.Date, clock.PhilippineToday, request.Remarks) is { } refused)
            return refused;

        if (monthly is not null && await monthly.HasActivatedPeriodAsync(new(request.Date.Year, request.Date.Month, 1), ct))
            return Result<bool>.Failure("Use the coordinated market-day reopening workflow to revise activated month credits.", ResultStatus.Conflict);

        var existing = await closureRepository.GetAsync(request.Date, ct);
        if (existing is not null)
        {
            closureRepository.Remove(existing);
            await unitOfWork.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, FacilityCode.NPM, request.Date.Year, request.Date.Month, ct);
        }
        return Result<bool>.Success(true);
    }
}
