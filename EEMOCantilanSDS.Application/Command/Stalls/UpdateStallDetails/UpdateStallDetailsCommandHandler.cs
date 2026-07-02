using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;

public class UpdateStallDetailsCommandHandler(
    IStallRepository stallRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<UpdateStallDetailsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateStallDetailsCommand request, CancellationToken cancellationToken)
    {
        var stall = await stallRepo.GetByIdAsync(request.StallId, cancellationToken);
        if (stall == null)
            return Result<bool>.NotFound();

        stall.UpdateDetails(
            request.ActualOccupant,
            request.NameOnContract,
            request.AreaSqm,
            request.AreaNote,
            null,
            "Admin");

        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        return Result<bool>.Success(true);
    }
}
