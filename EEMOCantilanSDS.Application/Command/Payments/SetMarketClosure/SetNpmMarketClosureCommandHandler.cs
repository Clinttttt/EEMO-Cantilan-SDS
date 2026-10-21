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

namespace EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;

/// <summary>
/// Closes the market for a day. A day the weekly schedule already closes needs no row, so closing it only cancels an earlier
/// opening of it. A change to a past day is the Head's and carries a reason; money already collected for the day is untouched.
/// </summary>
public class SetNpmMarketClosureCommandHandler(
    INpmMarketClosureRepository closureRepository,
    INpmOperatingScheduleRepository scheduleRepository,
    ICurrentUserService currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext, IAppDbContext? context = null, NpmRentMonthWorkflow? monthly = null) : IRequestHandler<SetNpmMarketClosureCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetNpmMarketClosureCommand request, CancellationToken ct)
    {
        if (NpmOperatingDayAuthority.Check(currentUser, request.Date, clock.PhilippineToday, request.Remarks) is { } refused)
            return refused;

        var coordinate = monthly is not null && await monthly.HasActivatedPeriodAsync(new(request.Date.Year, request.Date.Month, 1), ct);
        if (coordinate && (monthly!.Enabled != true || context is null))
            return Result<bool>.Failure("Activated NPM months require coordinated credit authority; no closure was changed.", ResultStatus.Conflict);
        await using var transaction = coordinate && !context!.HasActiveTransaction ? await context.BeginSerializableTransactionAsync(ct) : null;

        var recordedBy = currentUser.Username ?? "Admin";
        var scheduled = NpmOperatingCalendar.ScheduledClosed(request.Date, await scheduleRepository.GetAllAsync(ct));
        var existing = await closureRepository.GetAsync(request.Date, ct);

        if (existing is null)
        {
            // The schedule already closes this day: there is nothing to record.
            if (!scheduled)
                await closureRepository.AddAsync(NpmMarketClosure.Create(request.Date, request.Reason, request.Remarks, recordedBy), ct);
        }
        else if (existing.IsOpenOverride && scheduled)
        {
            // Closing a day the Head had opened simply returns it to the schedule.
            closureRepository.Remove(existing);
        }
        else
        {
            existing.Update(request.Reason, request.Remarks, recordedBy);
        }

        await unitOfWork.SaveChangesAsync(ct);
        if (coordinate)
        {
            await monthly!.SynchronizeOperatingCreditsAsync(new(request.Date.Year, request.Date.Month, 1),
                "Head market closure: " + request.Remarks, ct);
            await context!.SaveChangesAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, request.Date.Year, request.Date.Month, ct);

        return Result<bool>.Success(true);
    }
}
