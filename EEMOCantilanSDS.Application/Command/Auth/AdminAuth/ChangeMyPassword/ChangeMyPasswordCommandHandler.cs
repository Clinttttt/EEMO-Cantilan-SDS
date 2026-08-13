using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;

public class ChangeMyPasswordCommandHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    ITokenService tokenService,
    IUnitOfWork uow,
    IPasswordHasher passwordHasher) : IRequestHandler<ChangeMyPasswordCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(ChangeMyPasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actingId)
            return Result<TokenResponseDto>.Unauthorized();

        var admin = await adminRepo.GetByIdAsync(actingId, cancellationToken);
        if (admin is null) return Result<TokenResponseDto>.Unauthorized();

        // Re-authentication, exactly as the reset and two-factor paths do. Deliberately NOT skipped when the change is
        // required: an office-issued password may have been written down and handed over, so the person typing it is not
        // proven to be its owner until they can produce it.
        if (passwordHasher.Check(admin.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
            return Result<TokenResponseDto>.Failure("Your current password is incorrect.", 400);

        // Refused rather than accepted quietly: a required change that changes nothing leaves the account on a password
        // the office knows, which is the thing the requirement exists to end.
        if (passwordHasher.Check(admin.PasswordHash, request.NewPassword) != PasswordCheck.Failed)
            return Result<TokenResponseDto>.Failure("Your new password must be different from your current one.", 400);

        admin.ChangeOwnPassword(passwordHasher.Hash(request.NewPassword));

        // Issued AFTER the change, and in this order on purpose: accepting the password revokes the refresh token to sign
        // out other sessions, and creating the response then issues a fresh one for this session. The new access token
        // carries must_change_password = false, which is what releases the portal.
        var tokens = await tokenService.CreateTokenResponse(admin);

        await uow.SaveChangesAsync(cancellationToken);
        return Result<TokenResponseDto>.Success(tokens);
    }
}
