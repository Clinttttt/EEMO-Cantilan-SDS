using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;

public class ResetAdminPasswordCommandHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<ResetAdminPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetAdminPasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actingId)
            return Result<bool>.Unauthorized();

        var actor = await adminRepo.GetByIdAsync(actingId, cancellationToken);
        if (actor is null || !actor.VerifyPassword(request.ConfirmPassword))
            return Result<bool>.Failure("Your password is incorrect.", 400);

        var admin = await adminRepo.GetByIdAsync(request.AdminId, cancellationToken);
        if (admin is null) return Result<bool>.NotFound();

        admin.ResetPassword(request.NewPassword, currentUser.Username ?? "Admin");

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
