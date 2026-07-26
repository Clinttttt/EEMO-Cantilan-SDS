using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Auth.Mfa
{
    /// <summary>
    /// Second step of an MFA sign-in. Validates the challenge issued by the password step together with an
    /// authenticator code (or a single-use recovery code), and only then mints the session.
    /// <para>
    /// Security posture:
    /// <list type="bullet">
    /// <item>Uniform error for every failure — never reveals whether the challenge or the code was wrong.</item>
    /// <item>The challenge is matched by HASH, is single-use, and expires in minutes.</item>
    /// <item>Wrong codes feed the existing account lockout, so 6 digits cannot be brute-forced.</item>
    /// <item>An accepted TOTP step is recorded, so a code cannot be replayed within its own window.</item>
    /// <item>Re-checks IsActive at this step, so an account disabled between the two steps gets nothing.</item>
    /// </list>
    /// </para>
    /// Anonymous by necessity (no session exists yet); the API rate-limits it.
    /// </summary>
    public class VerifyMfaLoginCommandHandler(
        IAppDbContext context,
        ICredentialProtector protector,
        ITotpService totp,
        ITokenService tokenService)
        : IRequestHandler<VerifyMfaLoginCommand, Result<TokenResponseDto>>
    {
        // One message for every failure mode: unknown/expired/used challenge, wrong code, locked or
        // deactivated account.
        private const string GenericError = "That code is not valid, or the sign-in attempt expired. Please sign in again.";

        public async Task<Result<TokenResponseDto>> Handle(VerifyMfaLoginCommand request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ChallengeToken) || string.IsNullOrWhiteSpace(request.Code))
                return Result<TokenResponseDto>.Failure(GenericError, 400);

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.ChallengeToken)));

            // The challenge is globally unique and identifies its own tenant, so it is matched across all
            // municipalities (no tenant context exists before sign-in completes).
            var user = await context.AdminUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.MfaChallengeTokenHash == hash && !u.IsDeleted, ct);

            if (user is null || !user.IsMfaChallengeValid(hash))
                return Result<TokenResponseDto>.Failure(GenericError, 400);

            // State may have changed between the password step and now.
            if (!user.IsActive || user.IsLockedOut || !user.MfaEnabled || user.MfaSecretCipher is null)
            {
                user.ClearMfaChallenge();
                await context.SaveChangesAsync(ct);
                return Result<TokenResponseDto>.Failure(GenericError, 400);
            }

            var secret = protector.Unprotect(user.MfaSecretCipher);
            var code = request.Code.Trim();

            if (totp.TryValidate(secret, code, user.MfaLastUsedStep, out var step))
            {
                user.RecordMfaStep(step);          // closes the replay window behind this code
            }
            else if (user.TryConsumeRecoveryCode(RecoveryCodes.Hash(code)))
            {
                // Accepted a recovery code; it is now spent.
            }
            else
            {
                // Wrong second factor counts as a failed sign-in, so repeated guesses lock the account.
                user.RecordFailedLogin();
                await context.SaveChangesAsync(ct);
                return Result<TokenResponseDto>.Failure(GenericError, 400);
            }

            // Success: consume the challenge and clear any failed-attempt streak, then mint the session.
            user.ClearMfaChallenge();
            user.RecordLogin();

            var tokens = await tokenService.CreateTokenResponse(user);
            await context.SaveChangesAsync(ct);

            return Result<TokenResponseDto>.Success(tokens);
        }
    }
}
