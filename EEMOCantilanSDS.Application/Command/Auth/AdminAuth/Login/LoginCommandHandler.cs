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
        // Resolve the target tenant up-front when the caller specified which LGU it is signing into (scoped
        // login URL ?lgu={code}), so the username lookup is scoped to that municipality. A username shared
        // across LGUs otherwise resolves to an ARBITRARY tenant's account — the password is then checked
        // against the wrong hash and the legitimate admin is blocked (and the wrong account penalized).
        // When no code is supplied (direct /login, first-run setup, existing clients) the lookup stays
        // global — behaviour unchanged for the default Cantilan flow.
        Guid? scopeMunicipalityId = null;
        if (!string.IsNullOrWhiteSpace(request.MunicipalityCode))
        {
            var municipality = await municipalityRepository.GetByIdentifierAsync(request.MunicipalityCode, cancellationToken);
            if (municipality is null) return Result<TokenResponseDto>.Forbidden();
            scopeMunicipalityId = municipality.Id;
        }

        var user = scopeMunicipalityId is { } mid
            ? await authRepository.GetAdminByUsernameAsync(request.Username, mid, cancellationToken)
            : await authRepository.GetAdminByUsernameAsync(request.Username, cancellationToken);
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

        // Defense-in-depth: the account must belong to the requested LGU. With the scoped lookup above this
        // always holds; retained so no future non-scoped path can slip through. Checked AFTER the password
        // so it never reveals whether a username exists in another LGU.
        if (scopeMunicipalityId is { } boundaryId && boundaryId != user.MunicipalityId)
            return Result<TokenResponseDto>.Forbidden();

        user.RecordLogin();
        // CreateTokenResponse persists the reset login state together with the new refresh token.
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(user));
    }
}
