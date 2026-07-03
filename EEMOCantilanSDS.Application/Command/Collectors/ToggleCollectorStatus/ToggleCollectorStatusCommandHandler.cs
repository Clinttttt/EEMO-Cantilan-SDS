using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.ToggleCollectorStatus;

public class ToggleCollectorStatusCommandHandler(
    ICollectorRepository collectorRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<ToggleCollectorStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ToggleCollectorStatusCommand request, CancellationToken cancellationToken)
    {
        var collector = await collectorRepo.GetByIdAsync(request.CollectorId, cancellationToken);
        if (collector is null) return Result<bool>.NotFound();

        var actor = currentUser.Username ?? "Admin";
        if (request.Activate) collector.Activate(actor);
        else collector.Deactivate(actor);

        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);
        return Result<bool>.Success(true);
    }
}
