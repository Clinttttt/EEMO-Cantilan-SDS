using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;

public class UpdateStallCommandHandler(
    IStallRepository stallRepo,
    IUnitOfWork uow) : IRequestHandler<UpdateStallCommand, Result<StallDto>>
{
    public async Task<Result<StallDto>> Handle(UpdateStallCommand request, CancellationToken cancellationToken)
    {
        var stall = await stallRepo.GetByIdWithContractsAsync(request.StallId, cancellationToken);
        if (stall is null)
            return Result<StallDto>.NotFound();

        // Update stall rates and area
        stall.UpdateRates(request.MonthlyRate, request.DailyRate, "Admin");
        stall.UpdateAreaInfo(request.AreaSqm, request.AreaNote, request.Remarks, "Admin");

        // Update active contract occupant
        var activeContract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (activeContract is not null)
        {
            activeContract.UpdateOccupant(request.ActualOccupant, request.NameOnContract, "Admin");
        }

        await stallRepo.UpdateAsync(stall, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        var dto = new StallDto(
            stall.Id,
            stall.StallNo,
            stall.Status,
            request.ActualOccupant,
            request.NameOnContract,
            request.AreaSqm,
            activeContract?.EffectivityDate.ToDateTime(TimeOnly.MinValue),
            request.MonthlyRate,
            activeContract?.ORNumber,
            stall.Section,
            stall.AreaLocation,
            request.AreaNote,
            request.Remarks);

        return Result<StallDto>.Success(dto);
    }
}
