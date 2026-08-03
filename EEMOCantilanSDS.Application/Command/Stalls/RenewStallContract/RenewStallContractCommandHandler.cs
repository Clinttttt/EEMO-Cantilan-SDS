using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;

/// <summary>
/// Terminates the stall's current active contract(s) and adds a new term. History is preserved (the
/// old contract is kept, marked inactive); collected money is untouched. The new term defines the new
/// billing window — the lapsed period had no active contract, so it owes nothing.
/// </summary>
public class RenewStallContractCommandHandler(
    IStallRepository stallRepository,
    IPayorRepository payorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<RenewStallContractCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RenewStallContractCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdWithContractsAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        var actor = currentUser.Username ?? "Admin";

        // Corrections the office made on the renewal form, applied before the new term is written so the term
        // records the rate it is actually let at. Null means "as it stands": the rate, area and note are left
        // untouched, which is what plain "Proceed" does. The daily rate is carried through deliberately —
        // UpdateRates would otherwise clear it and a daily-billed space would lose its ordinance rate.
        if (request.MonthlyRate.HasValue && request.MonthlyRate.Value != stall.MonthlyRate)
            stall.UpdateRates(request.MonthlyRate.Value, stall.DailyRate, actor);

        if (request.AreaSqm.HasValue || request.AreaNote is not null)
            stall.UpdateAreaInfo(
                request.AreaSqm ?? stall.AreaSqm,
                request.AreaNote ?? stall.AreaNote,
                stall.Remarks,
                actor);

        // Capture the outgoing occupant BEFORE terminating, to detect a change of hands below.
        var previousOccupant = stall.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant;

        // End the current term(s); keep them as history (IsActive = false). The day before the new term starts is
        // the day the outgoing occupancy ended — recording it is what lets every historical view attribute money
        // and arrears to the lessee who actually held the stall then.
        foreach (var active in stall.Contracts.Where(c => c.IsActive).ToList())
            active.Terminate(actor, request.EffectivityDate.AddDays(-1));

        // Start the new term. The stall keeps its current rate unless the office corrected it above.
        var renewed = Contract.Create(
            stall.Id,
            request.ActualOccupant,
            request.NameOnContract,
            request.EffectivityDate,
            request.DurationYears,
            stall.MonthlyRate,
            createdBy: actor);

        await stallRepository.AddContractAsync(renewed, ct);

        // If the stall changed hands (different occupant), revoke any existing payor→stall links so the
        // OUTGOING occupant's online account can no longer view or pay the INCOMING occupant's dues. The
        // new occupant re-links by activating a fresh code. A same-occupant renewal keeps the link intact.
        var occupantChanged = !string.Equals(
            previousOccupant?.Trim(), request.ActualOccupant?.Trim(), StringComparison.OrdinalIgnoreCase);
        if (occupantChanged)
            await payorRepository.RemoveStallLinksAsync(stall.Id, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
