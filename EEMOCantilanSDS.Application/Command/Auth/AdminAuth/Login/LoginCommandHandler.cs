using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;

public class LoginCommandHandler(IAuthRepository authRepository, IMunicipalityRepository municipalityRepository, ITokenService tokenService, IUnitOfWork unitOfWork) : IRequestHandler<LoginCommand, Result<TokenResponseDto>>
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

        // Per-municipality login boundary. Only enforced when the caller specifies which LGU it is signing
        // into (scoped login URL ?lgu={code}); the account must belong to that LGU. Checked AFTER the
        // password so it never reveals whether a username exists in another LGU. When no code is supplied
        // (direct /login, the first-run setup flow, existing clients) this is skipped — behaviour unchanged.
        if (!string.IsNullOrWhiteSpace(request.MunicipalityCode))
        {
            var municipality = await municipalityRepository.GetByIdentifierAsync(request.MunicipalityCode, cancellationToken);
            if (municipality is null || municipality.Id != user.MunicipalityId)
                return Result<TokenResponseDto>.Forbidden();
        }

        user.RecordLogin();
        // CreateTokenResponse persists the reset login state together with the new refresh token.
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(user));
    }
}
