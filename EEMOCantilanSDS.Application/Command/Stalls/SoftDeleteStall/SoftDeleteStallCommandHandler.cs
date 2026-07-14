using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.SoftDeleteStall;

public class SoftDeleteStallCommandHandler(
    IStallRepository stallRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SoftDeleteStallCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SoftDeleteStallCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        // SAFETY GUARD: only an INACTIVE account may be removed — either CLOSED (frozen), or an active
        // stall whose LATEST contract term has already lapsed (expired). A current stall with a contract
        // still covering today (or a future-dated one) is never removable here. This keeps a live vendor
        // from ever being deleted from the roster.
        var today = PhilippineTime.Today;
        var isClosed = stall.Status == StallStatus.Closed;
        var activeContracts = stall.Contracts.Where(c => c.IsActive).ToList();
        var isExpired = activeContracts.Count > 0
            && activeContracts.Max(c => c.EffectivityDate.AddYears(c.DurationYears)) < today;

        if (!isClosed && !isExpired)
            return Result<bool>.Failure(
                "Only closed or expired accounts can be removed. This stall is still active.",
                409);

        // Soft-delete: hidden from every list + uniqueness (the number frees up), history retained.
        stall.SoftDelete(currentUser.Username ?? "Admin");
        await stallRepository.UpdateAsync(stall, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
