using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Command.Auth.Mfa
{
    /// <summary>
    /// Platform-operator rescue: clears two-factor on an account whose owner lost BOTH their authenticator
    /// device and their recovery codes.
    /// <para>
    /// This exists because a Head has nobody above them in their own LGU — peer Heads are blocked from each
    /// other's accounts, and self-service recovery restores a password, not a second factor. Without this,
    /// such a Head would be permanently locked out.
    /// </para>
    /// </summary>
    public record ResetUserMfaCommand(Guid UserId, string OperatorPassword) : IRequest<Result<bool>>;

    /// <summary>
    /// Clears the target's MFA after verifying the caller really is the platform operator AND re-entering
    /// their own password. Deliberately does NOT touch the target's password, role or active state: it only
    /// removes the second factor so the owner can sign in with their password and enrol again.
    /// </summary>
    public class ResetUserMfaCommandHandler(
        IAppDbContext context,
        ICurrentUserService currentUser,
        ILogger<ResetUserMfaCommandHandler> logger)
        : IRequestHandler<ResetUserMfaCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(ResetUserMfaCommand request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<bool>.Forbidden();

            if (currentUser.UserId is not { } operatorId)
                return Result<bool>.Unauthorized();

            // Re-authenticate the operator: clearing someone's second factor is a high-impact action, so a
            // hijacked operator session alone must not be enough.
            var operatorAccount = await context.AdminUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == operatorId && !u.IsDeleted, ct);

            if (operatorAccount is null)
                return Result<bool>.Unauthorized();

            if (string.IsNullOrEmpty(request.OperatorPassword) || !operatorAccount.VerifyPassword(request.OperatorPassword))
                return Result<bool>.Failure("Your password is incorrect.", 400);

            // Cross-tenant by design: the operator rescues accounts in any municipality.
            var target = await context.AdminUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, ct);

            if (target is null)
                return Result<bool>.NotFound();

            if (!target.MfaEnabled && !target.HasPendingMfaEnrollment)
                return Result<bool>.Failure("That account does not have two-factor authentication set up.", 400);

            target.DisableMfa();          // clears secret, recovery codes, replay marker and any challenge
            await context.SaveChangesAsync(ct);

            // High-impact security action: always leave a trail naming who did it and to whom.
            logger.LogWarning(
                "PLATFORM OPERATOR {Operator} cleared two-factor authentication for {Target} (account {TargetId})",
                operatorAccount.Username, target.Username, target.Id);

            return Result<bool>.Success(true);
        }
    }
}
