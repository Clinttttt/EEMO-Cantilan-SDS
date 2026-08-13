using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;

public class ResetAdminPasswordCommandHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IPasswordHasher passwordHasher) : IRequestHandler<ResetAdminPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetAdminPasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actingId)
            return Result<bool>.Unauthorized();

        var actor = await adminRepo.GetByIdAsync(actingId, cancellationToken);
        if (actor is null || passwordHasher.Check(actor.PasswordHash, request.ConfirmPassword) == PasswordCheck.Failed)
            return Result<bool>.Failure("Your password is incorrect.", 400);

        var admin = await adminRepo.GetByIdAsync(request.AdminId, cancellationToken);
        if (admin is null) return Result<bool>.NotFound();

        // A Head may not reset a PEER Head's password (only their own, or ordinary Admins). Re-authentication
        // above proves identity; this proves authority over the target.
        if (!AdminManagementGuard.CanActOn(admin, currentUser.UserId))
            return Result<bool>.Failure(AdminManagementGuard.PeerHeadDenied, 403);

        // Resetting YOUR OWN password is choosing it, not being issued one, so it does not then demand a change. The Head
        // who sets their own password would otherwise be marched to the change-password screen on their next sign-in to
        // replace a password they had just chosen.
        if (admin.Id == actingId)
            admin.ChangeOwnPassword(passwordHasher.Hash(request.NewPassword));
        else
            admin.ResetPassword(passwordHasher.Hash(request.NewPassword), currentUser.Username ?? "Admin");

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
