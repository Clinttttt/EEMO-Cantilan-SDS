using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;

public class LoginCommandHandler(IAuthRepository authRepository, IMunicipalityRepository municipalityRepository, ITokenService tokenService, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IClock clock) : IRequestHandler<LoginCommand, Result<TokenResponseDto>>
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
        // Uniform 401 for both unknown username and wrong password — never reveal which usernames exist.
        if (user is null) return Result<TokenResponseDto>.Unauthorized();

        var passwordOk = passwordHasher.Check(user.PasswordHash, request.Password) != PasswordCheck.Failed;

        // A locked account is told so — but only when the password is right. Someone guessing passwords keeps
        // getting the same blank 401 and so cannot use the lockout notice to discover that an account exists,
        // while the real owner is not left staring at "invalid credentials" for fifteen minutes. Attempts are
        // not counted while locked, so guessing cannot extend the lock indefinitely.
        if (user.IsLockedOut(clock.UtcNow))
        {
            if (!passwordOk) return Result<TokenResponseDto>.Unauthorized();

            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockedUntil!.Value - DateTime.UtcNow).TotalMinutes));
            return Result<TokenResponseDto>.Failure(
                $"This account is temporarily locked after {DomainRules.MaxFailedLoginAttempts} failed sign-in attempts. " +
                $"Please try again in {minutes} minute{(minutes == 1 ? "" : "s")}, or ask the office Head to reset your password.",
                ResultStatus.Locked);
        }

        if (!passwordOk)
        {
            user.RecordFailedLogin(clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // The attempt that trips the lock says so immediately, so the user is not left guessing why the
            // next five minutes of correct passwords are refused.
            if (user.IsLockedOut(clock.UtcNow))
                return Result<TokenResponseDto>.Failure(
                    $"This account is now locked for {DomainRules.LockoutMinutes} minutes after " +
                    $"{DomainRules.MaxFailedLoginAttempts} failed sign-in attempts.",
                    ResultStatus.Locked);

            return Result<TokenResponseDto>.Unauthorized();
        }

        if (!user.IsActive)
            return Result<TokenResponseDto>.Forbidden();

        // Defense-in-depth: the account must belong to the requested LGU. With the scoped lookup above this
        // always holds; retained so no future non-scoped path can slip through. Checked AFTER the password
        // so it never reveals whether a username exists in another LGU.
        if (scopeMunicipalityId is { } boundaryId && boundaryId != user.MunicipalityId)
            return Result<TokenResponseDto>.Forbidden();

        // The admin console admits the DEDICATED platform operator and nobody else.
        //
        // Checked after the password, like the boundary above, so it cannot be used to discover which accounts exist. The
        // flag is only set by the console's own endpoint; an LGU administrator signing into their own portal is unaffected.
        //
        // Reads `IsPlatformOperator` directly rather than going through PlatformOperatorPolicy. The policy used to be
        // broader — it also counted the SuperAdmin of the DEFAULT municipality, which is why the office Head of that
        // municipality could sign in here and see the onboarding console. That clause has since been retired, so the
        // two now agree; the flag is read directly because who may open the console is a question about the account
        // itself, and this check should not change again if the policy ever gains a clause.
        if (request.RequirePlatformOperator && !user.IsPlatformOperator)
            return Result<TokenResponseDto>.Failure(
                "This account is not a platform operator, so it cannot sign in to the onboarding console.",
                ResultStatus.Forbidden);

        // AND THE OTHER DIRECTION: the operator cannot sign in to a municipality's own portal.
        //
        // The operator account is created under the DEFAULT municipality "for tenant context" - see
        // CreateFirstConsoleAdminCommandHandler - so its MunicipalityId equals that LGU's. The boundary check above
        // therefore PASSED for the default municipality, and this endpoint does not require the operator flag: the
        // consequence was that the platform operator could sign in to the default LGU's console and read its
        // collections, vendors and reports. Another municipality was never reachable, because its id did not match, so
        // the exposure was one office - and it was the one office that never agreed to it.
        //
        // The operator's business is onboarding, activation and the platform's own tools, all of which live behind
        // console-login. Nothing it does requires a municipal session. An LGU's records belong to that LGU.
        //
        // Checked AFTER the password, exactly like the two above, so it cannot be used to discover that the operator
        // account exists or which username it holds.
        if (!request.RequirePlatformOperator && user is AdminUser { IsPlatformOperator: true })
            return Result<TokenResponseDto>.Failure(
                "The platform operator account cannot sign in to a municipality's portal. Use the onboarding console.",
                ResultStatus.Forbidden);

        user.RecordLogin();

        // Two-factor gate: the password is correct, but on an MFA-enabled account NO session is issued here.
        // Instead a short-lived, hashed, single-use challenge is stamped and returned; tokens are only minted
        // by the verify step once the authenticator code (or a recovery code) checks out. Accounts without
        // MFA are completely unaffected — the flow below is byte-for-byte the previous behaviour.
        if (user.MfaEnabled)
        {
            var (challenge, challengeHash) = GenerateChallenge();
            user.SetMfaChallenge(challengeHash, DateTime.UtcNow.AddMinutes(MfaChallengeMinutes));
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<TokenResponseDto>.Success(new TokenResponseDto
            {
                MfaRequired = true,
                MfaChallengeToken = challenge
            });
        }

        // CreateTokenResponse persists the reset login state together with the new refresh token.
        return Result<TokenResponseDto>.Success(await tokenService.CreateTokenResponse(user));
    }

    /// <summary>How long the user has to enter their authenticator code after the password step.</summary>
    private const int MfaChallengeMinutes = 5;

    // A url-safe, cryptographically-random single-use challenge; only its SHA-256 hash is stored.
    private static (string raw, string hash) GenerateChallenge()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return (raw, hash);
    }
}
