using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;

public class UpdateStallCommandHandler(
    IStallRepository stallRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<UpdateStallCommand, Result<StallDto>>
{
    public async Task<Result<StallDto>> Handle(UpdateStallCommand request, CancellationToken cancellationToken)
    {
        var stall = await stallRepo.GetByIdWithContractsAsync(request.StallId, cancellationToken);
        if (stall is null)
            return Result<StallDto>.NotFound();

        // A null DailyRate means "not supplied", NOT "clear it". Editing a stall from a screen that does
        // not show the daily rate (the stall profile, the generic vendor registry) previously wiped or
        // overwrote it: for a per-LGU CUSTOM section, whose own rate IS what billing charges via
        // Stall.ResolveDailyFee, a routine occupant-name edit could silently change the money. Only an
        // explicit value now moves the rate.
        stall.UpdateRates(request.MonthlyRate, request.DailyRate ?? stall.DailyRate, "Admin");
        stall.UpdateAreaInfo(request.AreaSqm, request.AreaNote, request.Remarks, "Admin");

        // Which charges apply to the space. The command carried this from the start but nothing ever wrote it,
        // so adding a utility charge on the vendor form appeared to save and changed nothing — the meter-reading
        // dialog went on saying the stall is not billed for electricity or water. Null still means "not
        // supplied", so a screen that does not edit the charges cannot strip one off the record.
        if (request.Fees is { } fees)
            stall.SetApplicableFees(fees, "Admin");

        // Update active contract occupant + terms
        var activeContract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (activeContract is not null)
        {
            activeContract.UpdateOccupant(request.ActualOccupant, request.NameOnContract, "Admin");

            if (request.ContractDate.HasValue && request.ContractYears.HasValue)
            {
                // A signed contract must run at least a year — the office's ruling of 2026-08-16 — and this is the only screen
                // that could ever have set one to nought, because it is the only one that edits an existing term. Answered with
                // a stated reason rather than left to the domain's exception, which would reach the office as a server error.
                //
                // Checked HERE and not in the validator because only this point knows the arrangement: the command does not
                // carry it, and nought is a legitimate value for an occupancy with no signed contract, which the stall DTO
                // reports for any stall without an active term.
                if (activeContract.Arrangement == OccupancyArrangement.SignedContract && request.ContractYears.Value < 1)
                {
                    return Result<StallDto>.Failure(
                        "A signed contract must run for at least one year. Record it as a space-only occupancy if there is no " +
                        "signed term.",
                        ResultStatus.Invalid);
                }

                activeContract.UpdateTerms(
                    DateOnly.FromDateTime(request.ContractDate.Value),
                    request.ContractYears.Value,
                    "Admin");
            }
        }

        await stallRepo.UpdateAsync(stall, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        var dto = new StallDto(
            stall.Id,
            stall.StallNo,
            stall.Status,
            request.ActualOccupant,
            request.NameOnContract,
            request.AreaSqm,
            activeContract?.EffectivityDate.ToDateTime(TimeOnly.MinValue),
            request.MonthlyRate,
            stall.DailyRate,      // the effective stored rate, which may have been preserved rather than set
            activeContract?.ORNumber,
            stall.Section,
            stall.AreaLocation,
            request.AreaNote,
            request.Remarks,
            CustomSectionName: stall.CustomSectionName);

        return Result<StallDto>.Success(dto);
    }
}
