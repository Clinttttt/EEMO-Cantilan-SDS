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
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<CreateStallCommand, Result<StallDto>>
{
    public async Task<Result<StallDto>> Handle(CreateStallCommand request, CancellationToken cancellationToken)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, cancellationToken);
        if (facility is null)
            return Result<StallDto>.NotFound();

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
        await uow.SaveChangesAsync(cancellationToken);

        var contract = Contract.Create(
            stall.Id,
            request.ActualOccupant,
            request.NameOnContract,
            DateOnly.FromDateTime(request.ContractDate ?? PhilippineTime.Now),
            request.ContractYears,
            request.MonthlyRate,
            null,
            null,
            "Admin");

        await stallRepo.AddContractAsync(contract, cancellationToken);
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
}
