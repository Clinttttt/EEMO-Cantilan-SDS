using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.ResetCollectorPassword;

public class ResetCollectorPasswordCommandHandler(
    ICollectorRepository collectorRepo,
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<ResetCollectorPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetCollectorPasswordCommand request, CancellationToken cancellationToken)
    {
        // Re-authenticate the acting Head before resetting a collector's mobile credentials.
        if (currentUser.UserId is not { } actingId)
            return Result<bool>.Unauthorized();

        var actor = await adminRepo.GetByIdAsync(actingId, cancellationToken);
        if (actor is null || !actor.VerifyPassword(request.ConfirmPassword))
            return Result<bool>.Failure("Your password is incorrect.", 400);

        var collector = await collectorRepo.GetByIdAsync(request.CollectorId, cancellationToken);
        if (collector is null) return Result<bool>.NotFound();

        collector.ResetPassword(request.NewPassword, currentUser.Username ?? "Admin");

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
