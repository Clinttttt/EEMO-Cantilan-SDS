using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.CreateStall;

public class CreateStallCommandHandler(
    IStallRepository stallRepo,
    IFacilityRepository facilityRepo,
    IPayorRepository payorRepository,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext, IClock clock) : IRequestHandler<CreateStallCommand, Result<StallDto>>
{
    public async Task<Result<StallDto>> Handle(CreateStallCommand request, CancellationToken cancellationToken)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, cancellationToken);
        if (facility is null)
            return Result<StallDto>.NotFound();

        // Taking over a stall the office has vacated. The physical space keeps its number, its section and its
        // whole history: the previous terms stay as history, the money already collected against it is untouched,
        // and a new contract simply begins. This is why a market with 23 stalls never grows a "Stall 24" just
        // because a lessee left — the number belongs to the space, not to whoever occupies it.
        if (request.ReuseVacatedStall)
        {
            var existing = await stallRepo.FindStallByNumberAsync(
                request.FacilityCode, request.Section, request.CustomSectionName, request.StallNo, cancellationToken);

            if (existing is not null)
            {
                if (!existing.IsVacant(clock.PhilippineToday))
                    return Result<StallDto>.Failure("That stall is still occupied, so it cannot be reassigned.", 409);

                return await ReassignAsync(existing, request, cancellationToken);
            }
        }

        var stall = Stall.Create(
            facility.Id,
            request.StallNo,
            request.MonthlyRate,
            request.Fees,
            request.Section,
            request.AreaLocation,
            request.AreaSqm,
            request.AreaNote,
            request.DailyRate,
            null,
            createdBy: "Admin",
            customSectionName: request.CustomSectionName);

        // Register a brand-new NPM custom section so it becomes a reusable dropdown option going forward
        // (no-op if it already exists). Only for NPM custom-section stalls; canonical stalls are unaffected.
        if (facility.Code == FacilityCode.NPM && !string.IsNullOrWhiteSpace(request.CustomSectionName))
            facility.AddCustomSection(request.CustomSectionName, "Admin");

        await stallRepo.AddAsync(stall, cancellationToken);

        var contract = Contract.Create(
            stall.Id,
            request.ActualOccupant,
            request.NameOnContract,
            DateOnly.FromDateTime(request.ContractDate ?? clock.PhilippineNow),
            request.ContractYears,
            request.MonthlyRate,
            null,
            null,
            "Admin",
            request.Arrangement);

        await stallRepo.AddContractAsync(contract, cancellationToken);

        // ONE commit for the space and its first term. Saving the stall first and the contract after left a window in
        // which a failure produced a let space with no agreement behind it: it would appear on the register, answer for
        // a month's rent, and have no lessee, no term and no start date to bill against. The stall's id is generated in
        // memory, so nothing here needs the first insert to have happened.
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        var dto = new StallDto(
            stall.Id,
            request.StallNo,
            StallStatus.Active,
            request.ActualOccupant,
            request.NameOnContract,
            request.AreaSqm,
            request.ContractDate,
            request.MonthlyRate,
            request.DailyRate,
            null,
            request.Section,
            request.AreaLocation,
            request.AreaNote,
            null,
            CustomSectionName: stall.CustomSectionName
            );

        return Result<StallDto>.Success(dto);
    }

    /// <summary>
    /// Hands a vacated stall to a new lessee: end any lingering term (kept as history), reopen the space if it
    /// had been closed, apply the new rates/fees/area from the form, and start the new contract. Nothing is
    /// deleted or renumbered, so every past contract, collection and receipt stays attached to this stall.
    /// </summary>
    private async Task<Result<StallDto>> ReassignAsync(Stall stall, CreateStallCommand request, CancellationToken ct)
    {
        const string actor = "Admin";

        var newStart = DateOnly.FromDateTime(request.ContractDate ?? clock.PhilippineNow);

        // The outgoing occupancy ended the day before the incoming one begins. Dating it is what keeps each
        // lessee's collections and arrears on their own account once the stall has changed hands.
        foreach (var lingering in stall.Contracts.Where(c => c.IsActive).ToList())
            lingering.Terminate(actor, newStart.AddDays(-1));

        if (!stall.IsActive())
            stall.Reopen(actor);

        stall.UpdateRates(request.MonthlyRate, request.DailyRate, actor);
        stall.UpdateAreaInfo(request.AreaSqm, request.AreaNote, null, actor);
        stall.AddUtilityFees(
            request.Fees.HasFlag(ApplicableFees.Electricity),
            request.Fees.HasFlag(ApplicableFees.Water),
            actor);

        var contract = Contract.Create(
            stall.Id,
            request.ActualOccupant,
            request.NameOnContract,
            newStart,
            request.ContractYears,
            request.MonthlyRate,
            null,
            null,
            actor,
            request.Arrangement);

        await stallRepo.AddContractAsync(contract, ct);

        // The space changed hands: any payor account still linked to it belonged to the previous lessee and must
        // not see or pay the new lessee's dues. The incoming lessee links again with a fresh activation code.
        await payorRepository.RemoveStallLinksAsync(stall.Id, ct);

        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<StallDto>.Success(new StallDto(
            stall.Id,
            stall.StallNo,
            stall.Status,
            request.ActualOccupant,
            request.NameOnContract,
            stall.AreaSqm,
            request.ContractDate,
            stall.MonthlyRate,
            stall.DailyRate,
            null,
            stall.Section,
            stall.AreaLocation,
            stall.AreaNote,
            null,
            CustomSectionName: stall.CustomSectionName));
    }
}
