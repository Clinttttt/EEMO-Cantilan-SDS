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
                423);
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
                    423);

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
