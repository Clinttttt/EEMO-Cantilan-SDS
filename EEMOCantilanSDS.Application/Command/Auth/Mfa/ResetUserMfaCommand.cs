using EEMOCantilanSDS.Application.Common.Interface.Security;
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
    /// Clears two-factor on an account whose owner lost BOTH their authenticator device and their recovery
    /// codes.
    /// <para>
    /// An office administers its own staff: a Head clears the second factor for accounts in their OWN
    /// municipality, under the same peer-Head rule that governs every other admin-management action
    /// (<see cref="AdminManagementGuard"/>) — their own account and ordinary Admin accounts, never another
    /// Head's. Reaching across municipalities stays a platform-operator power, and it is the rescue that
    /// matters: an office whose only Head is locked out has nobody of its own left to ask.
    /// </para>
    /// </summary>
    public record ResetUserMfaCommand(Guid UserId, string OperatorPassword) : IRequest<Result<bool>>;

    /// <summary>
    /// Clears the target's MFA after establishing that the caller may act on that account AND re-entering
    /// their own password. Deliberately does NOT touch the target's password, role or active state: it only
    /// removes the second factor so the owner can sign in with their password and enrol again.
    /// </summary>
    public class ResetUserMfaCommandHandler(
        IAppDbContext context,
        ICurrentUserService currentUser,
        ILogger<ResetUserMfaCommandHandler> logger,
    IPasswordHasher passwordHasher)
        : IRequestHandler<ResetUserMfaCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(ResetUserMfaCommand request, CancellationToken ct)
        {
            // Who may clear a second factor at all: a dedicated platform operator (any LGU, because rescuing an
            // office's only Head is its job), or a municipality's own Head (their own office only, enforced
            // below). This used to require platform-operator for BOTH, which left every LGU except the default
            // one unable to administer its own staff: a Madrid clerk who lost their phone had to be rescued by
            // the platform, and the platform's own office was the only one that could help itself.
            var isDedicatedOperator = await PlatformOperatorGuard.IsDedicatedOperatorAsync(context, currentUser, ct);
            var isHead = string.Equals(currentUser.Role, PlatformOperatorPolicy.SuperAdminRole, StringComparison.OrdinalIgnoreCase);
            if (!isDedicatedOperator && !isHead)
                return Result<bool>.Forbidden();

            if (currentUser.UserId is not { } actingUserId)
                return Result<bool>.Unauthorized();

            // Re-authenticate the caller: clearing someone's second factor is a high-impact action, so a
            // hijacked session alone must not be enough.
            var actingAccount = await context.AdminUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == actingUserId && !u.IsDeleted, ct);

            if (actingAccount is null)
                return Result<bool>.Unauthorized();

            if (string.IsNullOrEmpty(request.OperatorPassword) || passwordHasher.Check(actingAccount.PasswordHash, request.OperatorPassword) == PasswordCheck.Failed)
                return Result<bool>.Failure("Your password is incorrect.", ResultStatus.Invalid);

            var target = await context.AdminUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, ct);

            if (target is null)
                return Result<bool>.NotFound();

            if (!isDedicatedOperator)
            {
                // Another municipality's accounts are not this Head's to touch. Answered as NotFound rather
                // than Forbidden so the response never confirms that an account outside the caller's own
                // office exists.
                if (target.MunicipalityId != currentUser.MunicipalityId)
                {
                    logger.LogWarning(
                        "Head {Actor} attempted to clear two-factor for account {TargetId} in another municipality",
                        actingAccount.Username, target.Id);
                    return Result<bool>.NotFound();
                }

                // Inside their own office, the ordinary peer-Head rule applies: a Head may act on their own
                // account and on Admin accounts, never on another Head's. A locked-out Head is the platform
                // operator's rescue, which is why that rescue exists.
                if (!AdminManagementGuard.CanActOn(target, actingUserId))
                    return Result<bool>.Failure(AdminManagementGuard.PeerHeadDenied, ResultStatus.Forbidden);
            }

            if (!target.MfaEnabled && !target.HasPendingMfaEnrollment)
                return Result<bool>.Failure("That account does not have two-factor authentication set up.", ResultStatus.Invalid);

            target.DisableMfa();          // clears secret, recovery codes, replay marker and any challenge
            await context.SaveChangesAsync(ct);

            // High-impact security action: always leave a trail naming who did it, to whom, and under which
            // authority — an office's own Head and the platform operator are not the same actor.
            logger.LogWarning(
                "{Authority} {Actor} cleared two-factor authentication for {Target} (account {TargetId})",
                isDedicatedOperator ? "PLATFORM OPERATOR" : "HEAD",
                actingAccount.Username, target.Username, target.Id);

            return Result<bool>.Success(true);
        }
    }
}
