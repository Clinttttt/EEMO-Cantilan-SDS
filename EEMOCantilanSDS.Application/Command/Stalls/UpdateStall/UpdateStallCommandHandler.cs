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

        // Update stall rates and area
        stall.UpdateRates(request.MonthlyRate, request.DailyRate, "Admin");
        stall.UpdateAreaInfo(request.AreaSqm, request.AreaNote, request.Remarks, "Admin");

        // Update active contract occupant + terms
        var activeContract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (activeContract is not null)
        {
            activeContract.UpdateOccupant(request.ActualOccupant, request.NameOnContract, "Admin");

            if (request.ContractDate.HasValue && request.ContractYears.HasValue)
            {
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
            request.DailyRate,
            activeContract?.ORNumber,
            stall.Section,
            stall.AreaLocation,
            request.AreaNote,
            request.Remarks,
            CustomSectionName: stall.CustomSectionName);

        return Result<StallDto>.Success(dto);
    }
}
