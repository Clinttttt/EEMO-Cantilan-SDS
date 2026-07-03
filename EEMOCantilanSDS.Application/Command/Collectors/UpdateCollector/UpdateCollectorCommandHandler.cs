using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;

public class UpdateCollectorCommandHandler(
    ICollectorRepository collectorRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<UpdateCollectorCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateCollectorCommand request, CancellationToken cancellationToken)
    {
        var collector = await collectorRepo.GetByIdAsync(request.CollectorId, cancellationToken);
        if (collector is null) return Result<bool>.NotFound();

        collector.UpdateProfile(request.FullName, request.ContactNumber, request.Email, currentUser.Username ?? "Admin");
        await collectorRepo.ReplaceFacilityAssignmentsAsync(request.CollectorId, request.AssignedFacilities, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        return Result<bool>.Success(true);
    }
}
