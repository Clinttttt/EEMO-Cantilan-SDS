using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;

public class LoginCommandHandler(IAuthRepository authRepository, ITokenService tokenService, IUnitOfWork unitOfWork) : IRequestHandler<LoginCommand, Result<TokenResponseDto>>
{
    public async Task<Result<TokenResponseDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await authRepository.GetAdminByUsernameAsync(request.Username, cancellationToken);
        if (user is null) return Result<TokenResponseDto>.NotFound();

        if (user.IsLockedOut)
            return Result<TokenResponseDto>.Unauthorized();

        if (new PasswordHasher<BaseUser>().VerifyHashedPassword(user, user.PasswordHash, request.Password)
            == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TokenResponseDto>.Unauthorized();
        }

        if (!user.IsActive)
            return Result<TokenResponseDto>.Forbidden();

        user.RecordLogin();
        // CreateTokenResponse persists the reset login state together with the new refresh token.
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(user));
    }
}
