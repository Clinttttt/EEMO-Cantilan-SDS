using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;

/// <summary>
/// Copies the billing shape of the stall a returning payor used to hold — facility, section, which fees apply, the
/// daily rate, the area — onto a new stall, and hands the work to <see cref="CreateStallCommand"/> so registration
/// goes through exactly the same validation, uniqueness rule and audit trail as any stall the office adds by hand.
///
/// Nothing about the previous stall is written. This is deliberate: the balance on the closed account belongs to
/// the term that incurred it, and the receipts that settle it reference that account.
/// </summary>
public class AssignPastOccupantStallCommandHandler(
    IStallRepository stallRepo,
    ISender sender) : IRequestHandler<AssignPastOccupantStallCommand, Result<StallDto>>
{
    public async Task<Result<StallDto>> Handle(AssignPastOccupantStallCommand request, CancellationToken ct)
    {
        var previous = await stallRepo.GetByIdWithContractsAsync(request.PreviousStallId, ct);
        if (previous is null)
            return Result<StallDto>.NotFound();

        var facilityCode = await stallRepo.GetFacilityCodeByStallIdAsync(request.PreviousStallId, ct);
        if (facilityCode is null)
            return Result<StallDto>.NotFound();

        // Whose placement this is — the term named by the caller, not simply the stall's latest, which on a re-let
        // stall belongs to the lessee sitting there now.
        var lastContract = PastOccupancyContract.Resolve(previous, request.ContractId);

        if (lastContract is null)
            return Result<StallDto>.Failure("That stall has no contract to read the lessee from.", 409);

        // A daily-billed stall must carry a daily rate, and some older records do not. Rather than fail the
        // registration, fall back to the LGU's currently-effective rate — the same figure the Add Vendor form uses.
        var dailyRate = previous.DailyRate;
        if (dailyRate is null or <= 0 && facilityCode.Value == Domain.Enums.FacilityCode.NPM)
        {
            var rates = await sender.Send(new GetNpmRatesQuery(), ct);
            if (rates.IsSuccess && rates.Value is { DailyRate: > 0 })
                dailyRate = rates.Value.DailyRate;
        }

        return await sender.Send(new CreateStallCommand(
            facilityCode.Value,
            request.StallNo,
            request.MonthlyRate,
            previous.Fees,
            previous.Section,
            previous.AreaLocation,
            previous.AreaSqm,
            previous.AreaNote,
            dailyRate,
            lastContract.ActualOccupant,
            string.IsNullOrWhiteSpace(request.NameOnContract) ? lastContract.NameOnContract : request.NameOnContract,
            request.ContractDate,
            request.ContractYears,
            previous.CustomSectionName), ct);
    }
}
