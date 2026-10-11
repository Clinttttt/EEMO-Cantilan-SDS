using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;

public class ClearNpmMarketClosureCommandHandler(
    INpmMarketClosureRepository closureRepository,
    INpmOperatingScheduleRepository scheduleRepository,
    ICurrentUserService currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<ClearNpmMarketClosureCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ClearNpmMarketClosureCommand request, CancellationToken ct)
    {
        if (NpmOperatingDayAuthority.Check(currentUser, request.Date, clock.PhilippineToday, request.Remarks) is { } refused)
            return refused;

        var recordedBy = currentUser.Username ?? "Admin";
        var scheduled = NpmOperatingCalendar.ScheduledClosed(request.Date, await scheduleRepository.GetAllAsync(ct));
        var existing = await closureRepository.GetAsync(request.Date, ct);
        var changed = false;

        if (scheduled)
        {
            // The schedule closes this day, so opening it is an explicit opening of that one day.
            if (existing is null)
            {
                await closureRepository.AddAsync(NpmMarketClosure.CreateOpenOverride(request.Date, request.Remarks, recordedBy), ct);
                changed = true;
            }
            else if (!existing.IsOpenOverride)
            {
                existing.MakeOpenOverride(request.Remarks, recordedBy);
                changed = true;
            }
        }
        else if (existing is not null)
        {
            closureRepository.Remove(existing);
            changed = true;
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, FacilityCode.NPM, request.Date.Year, request.Date.Month, ct);
        }
        return Result<bool>.Success(true);
    }
}
