using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Command.Backup.RestoreTenantData;

/// <summary>
/// Enforces every guardrail SERVER-SIDE and in order: authenticated → SuperAdmin (Head) → exact "RESTORE"
/// phrase → admin password re-verified → parse the uploaded snapshot → atomic scoped restore (the
/// repository additionally rejects a snapshot for a different municipality, and runs in one transaction so
/// any failure rolls back with zero changes). The password is never logged.
/// </summary>
public class RestoreTenantDataCommandHandler(
    ICurrentUserService currentUser,
    IAuthRepository authRepository,
    ITenantRestoreRepository restoreRepository,
    ILogger<RestoreTenantDataCommandHandler> logger)
    : IRequestHandler<RestoreTenantDataCommand, Result<TenantRestoreResult>>
{
    public async Task<Result<TenantRestoreResult>> Handle(RestoreTenantDataCommand request, CancellationToken ct)
    {
        var username = currentUser.Username;
        if (string.IsNullOrWhiteSpace(username))
            return Result<TenantRestoreResult>.Unauthorized();

        if (currentUser.Role != "SuperAdmin")
            return Result<TenantRestoreResult>.Forbidden();

        if (!string.Equals(request.ConfirmationPhrase?.Trim(), "RESTORE", StringComparison.Ordinal))
            return Result<TenantRestoreResult>.Failure("Type RESTORE to confirm.", 400);

        var admin = await authRepository.GetAdminByUsernameAsync(username, ct);
        if (admin is null)
            return Result<TenantRestoreResult>.Unauthorized();

        if (new PasswordHasher<BaseUser>().VerifyHashedPassword(admin, admin.PasswordHash, request.Password)
            == PasswordVerificationResult.Failed)
            return Result<TenantRestoreResult>.Failure("Password is incorrect.", 401);

        if (request.SnapshotJson is null || request.SnapshotJson.Length == 0)
            return Result<TenantRestoreResult>.Failure("No backup file was provided.", 400);

        TenantRestoreSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TenantRestoreSnapshot>(request.SnapshotJson);
        }
        catch
        {
            return Result<TenantRestoreResult>.Failure("The backup file is not a valid restore snapshot.", 400);
        }

        if (snapshot is null)
            return Result<TenantRestoreResult>.Failure("The backup file is not a valid restore snapshot.", 400);

        try
        {
            var result = await restoreRepository.RestoreAsync(snapshot, ct);
            logger.LogWarning("Tenant restore performed by {Username}: {Rows} rows across {Tables} tables.",
                username, result.RowsRestored, result.TablesRestored);
            return Result<TenantRestoreResult>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            // Validation/guard failures from the repository (wrong tenant, wrong format, no tenant). Nothing was written.
            return Result<TenantRestoreResult>.Failure(ex.Message, 400);
        }
        catch (Exception ex)
        {
            // Any DB error → the transaction rolled back; the municipality's data is unchanged.
            logger.LogError(ex, "Tenant restore failed for {Username}; transaction rolled back.", username);
            return Result<TenantRestoreResult>.Failure(
                "The restore could not be completed and was rolled back — your data was not changed.", 500);
        }
    }
}
