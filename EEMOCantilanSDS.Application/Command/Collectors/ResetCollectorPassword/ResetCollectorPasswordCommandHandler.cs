using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.ResetCollectorPassword;

public class ResetCollectorPasswordCommandHandler(
    ICollectorRepository collectorRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<ResetCollectorPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetCollectorPasswordCommand request, CancellationToken cancellationToken)
    {
        var collector = await collectorRepo.GetByIdAsync(request.CollectorId, cancellationToken);
        if (collector is null) return Result<bool>.NotFound();

        collector.ResetPassword(request.NewPassword, currentUser.Username ?? "Admin");

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
