using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken
{
    /// <summary>
    /// Consumes a one-time password-reset token and sets the new password.
    /// <para>
    /// The token is looked up by HASH (the raw value is never stored), validated in fixed time, and consumed
    /// on success. The account's active state is never changed by this flow, so a reset link can never
    /// re-enable a deactivated account. Every existing session is invalidated after the change.
    /// </para>
    /// </summary>
    public class ResetPasswordByTokenCommandHandler(IAppDbContext context, IEmailSender emailSender, IClock clock, IPasswordHasher passwordHasher)
        : IRequestHandler<ResetPasswordByTokenCommand, Result<bool>>
    {
        // One message for every failure mode: unknown token, expired token, already-used token, or a
        // disabled account. Never reveals which.
        private const string GenericError = "This password reset link is invalid or has expired.";

        public async Task<Result<bool>> Handle(ResetPasswordByTokenCommand request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Result<bool>.Failure(GenericError);

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

            // Anonymous flow: the token is the secret and is globally unique, so it is matched across all
            // municipalities — the token itself determines the tenant (it was issued to exactly one account).
            var user = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == hash && !u.IsDeleted, ct);

            // IsPasswordResetTokenValid re-checks the hash in fixed time and enforces the expiry.
            if (user is null || !user.IsPasswordResetTokenValid(hash, clock.UtcNow))
                return Result<bool>.Failure(GenericError);

            // A deactivated account must not be recoverable through a reset link. The token is consumed
            // anyway so a stale link cannot be retried.
            if (!user.IsActive)
            {
                user.ClearPasswordResetToken();
                await context.SaveChangesAsync(ct);
                return Result<bool>.Failure(GenericError);
            }

            // Domain hashes the password, consumes the token (single use), clears any lockout, and revokes
            // the refresh token so existing sessions are signed out.
            user.CompletePasswordReset(passwordHasher.Hash(request.NewPassword));
            await context.SaveChangesAsync(ct);

            // Security notification (OWASP): tell the owner their password changed, so an unauthorized reset
            // is noticed. Best-effort — never fails the reset.
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                var body =
                    $"The password for your StallTrack account ({user.Username}) was just changed using a " +
                    "password reset link.\n\n" +
                    "If this was you, no further action is needed — you can now sign in with your new " +
                    "password.\n\n" +
                    "If this was NOT you, contact your office Head immediately to have your account secured.\n\n" +
                    "— StallTrack";
                await emailSender.SendAsync(user.Email!, user.FullName, "StallTrack — your password was changed", body, ct);
            }

            return Result<bool>.Success(true);
        }
    }
}
