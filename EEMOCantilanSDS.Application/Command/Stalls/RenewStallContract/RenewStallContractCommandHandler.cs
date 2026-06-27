using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
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
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<RenewStallContractCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RenewStallContractCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdWithContractsAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        var actor = currentUser.Username ?? "Admin";

        // End the current term(s); keep them as history (IsActive = false).
        foreach (var active in stall.Contracts.Where(c => c.IsActive).ToList())
            active.Terminate(actor);

        // Start the new term. The stall keeps its current rate.
        var renewed = Contract.Create(
            stall.Id,
            request.ActualOccupant,
            request.NameOnContract,
            request.EffectivityDate,
            request.DurationYears,
            stall.MonthlyRate,
            createdBy: actor);

        await stallRepository.AddContractAsync(renewed, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
