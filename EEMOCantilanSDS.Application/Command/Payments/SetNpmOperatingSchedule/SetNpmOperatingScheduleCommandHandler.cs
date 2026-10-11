using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SetNpmOperatingSchedule;

public class SetNpmOperatingScheduleCommandHandler(
    INpmOperatingScheduleRepository scheduleRepository,
    ICurrentUserService currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SetNpmOperatingScheduleCommand, Result<bool>>
{
    public const int MinimumAuthorityLength = 3;

    public async Task<Result<bool>> Handle(SetNpmOperatingScheduleCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "SuperAdmin")
            return Result<bool>.Forbidden();

        var today = clock.PhilippineToday;
        if (request.EffectiveFrom < today)
            return Result<bool>.Failure("A weekly schedule starts today or later. Days that have passed are not re-read under it.", ResultStatus.Invalid);

        var closed = request.ClosedWeekdays.Distinct().ToList();
        if (closed.Count >= 7)
            return Result<bool>.Failure("The market must open on at least one weekday.", ResultStatus.Invalid);
        if (closed.Count > 0 && (request.Remarks?.Trim().Length ?? 0) < MinimumAuthorityLength)
            return Result<bool>.Failure("State the authority for closing these weekdays.", ResultStatus.Invalid);

        var recordedBy = currentUser.Username ?? "Head";
        var existing = await scheduleRepository.GetByEffectiveDateAsync(request.EffectiveFrom, ct);
        if (existing is null)
        {
            await scheduleRepository.AddAsync(NpmOperatingSchedule.Create(request.EffectiveFrom, closed, request.Remarks, recordedBy), ct);
        }
        else if (existing.EffectiveFrom <= today)
        {
            // A version already in force is history. A change is a new version from a later date, never an edit of this one.
            return Result<bool>.Failure("A schedule that is already in force cannot be edited. Set a new one from a later date.", ResultStatus.Conflict);
        }
        else
        {
            existing.Revise(closed, request.Remarks, recordedBy);
        }

        await unitOfWork.SaveChangesAsync(ct);
        // The pattern reaches every later month's calendar, ledger and report, so every cached view of the tenant is stale.
        await cacheInvalidator.InvalidateTenantAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
