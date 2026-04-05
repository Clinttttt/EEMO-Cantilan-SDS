using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.CreateStall;

public class CreateStallCommandHandler(
    IStallRepository stallRepo,
    IFacilityRepository facilityRepo,
    IUnitOfWork uow) : IRequestHandler<CreateStallCommand, Result<StallDto>>
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
            "Admin");

        await stallRepo.AddAsync(stall, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        var contract = Contract.Create(
            stall.Id,
            request.ActualOccupant,
            request.NameOnContract,
            DateOnly.FromDateTime(request.ContractDate ?? DateTime.Today),
            request.ContractYears,
            request.MonthlyRate,
            null,
            null,
            "Admin");

        await stallRepo.AddContractAsync(contract, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        var dto = new StallDto(
            stall.Id,
            request.StallNo,
            StallStatus.Active,
            request.ActualOccupant,
            request.NameOnContract,
            request.AreaSqm,
            request.ContractDate,
            request.MonthlyRate,
            null,
            request.Section,
            request.AreaLocation);

        return Result<StallDto>.Success(dto);
    }
}
