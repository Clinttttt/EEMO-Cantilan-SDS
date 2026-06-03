using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.ToggleCollectorStatus;

public class ToggleCollectorStatusCommandHandler(
    ICollectorRepository collectorRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<ToggleCollectorStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ToggleCollectorStatusCommand request, CancellationToken cancellationToken)
    {
        var collector = await collectorRepo.GetByIdAsync(request.CollectorId, cancellationToken);
        if (collector is null) return Result<bool>.NotFound();

        var actor = currentUser.Username ?? "Admin";
        if (request.Activate) collector.Activate(actor);
        else collector.Deactivate(actor);

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
