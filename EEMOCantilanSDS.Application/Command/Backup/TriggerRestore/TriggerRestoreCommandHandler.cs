using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Command.Backup.TriggerRestore;

/// <summary>
/// Handles the destructive restore request. Enforces every guardrail SERVER-SIDE and in order:
/// authenticated → SuperAdmin (Head) only → exact "RESTORE" phrase → admin password re-verified
/// (same scheme as login) → dispatch the restore workflow. The password is NEVER logged; a warning
/// audit line captures who triggered the restore only after a successful dispatch.
/// </summary>
public class TriggerRestoreCommandHandler(
    ICurrentUserService currentUser,
    IAuthRepository authRepository,
    IBackupService backupService,
    ILogger<TriggerRestoreCommandHandler> logger)
    : IRequestHandler<TriggerRestoreCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(TriggerRestoreCommand request, CancellationToken cancellationToken)
    {
        // 1) Must be an authenticated user.
        var username = currentUser.Username;
        if (string.IsNullOrWhiteSpace(username))
            return Result<bool>.Unauthorized();

        // 2) Head-only (SuperAdmin). Defense-in-depth alongside the [Authorize] on the endpoint.
        if (currentUser.Role != "SuperAdmin")
            return Result<bool>.Forbidden();

        // 3) Exact confirmation phrase — case-SENSITIVE, ordinal. Never trust the client's own check.
        if (!string.Equals(request.ConfirmationPhrase?.Trim(), "RESTORE", StringComparison.Ordinal))
            return Result<bool>.Failure("Type RESTORE to confirm.", 400);

        // 4) Re-fetch the acting admin to re-verify their password against the stored hash.
        var admin = await authRepository.GetAdminByUsernameAsync(username, cancellationToken);
        if (admin is null)
            return Result<bool>.Unauthorized();

        // 5) Re-authenticate: verify the password exactly like LoginCommandHandler does.
        if (new PasswordHasher<BaseUser>().VerifyHashedPassword(admin, admin.PasswordHash, request.Password)
            == PasswordVerificationResult.Failed)
            return Result<bool>.Failure("Password is incorrect.", 401);

        // 6) All guardrails passed — dispatch the destructive restore workflow.
        var result = await backupService.TriggerRestoreAsync(cancellationToken);

        // 7) Audit trail on success. Never log the password.
        if (result.IsSuccess)
            logger.LogWarning("Database restore triggered by admin {Username}", username);

        return result;
    }
}
