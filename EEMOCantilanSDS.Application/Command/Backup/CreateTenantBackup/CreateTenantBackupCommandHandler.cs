using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Command.Backup.CreateTenantBackup;

public class CreateTenantBackupCommandHandler(
    ICurrentUserService currentUser,
    ITenantBackupRepository backupRepository,
    ILogger<CreateTenantBackupCommandHandler> logger)
    : IRequestHandler<CreateTenantBackupCommand, Result<TenantBackupInfo>>
{
    public async Task<Result<TenantBackupInfo>> Handle(CreateTenantBackupCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Username))
            return Result<TenantBackupInfo>.Unauthorized();
        if (currentUser.Role != "SuperAdmin")
            return Result<TenantBackupInfo>.Forbidden();

        try
        {
            var info = await backupRepository.CreateAsync(request.Note, ct);
            logger.LogInformation("Tenant backup created by {User}: {Rows} rows across {Tables} tables.",
                currentUser.Username, info.RowCount, info.TableCount);
            return Result<TenantBackupInfo>.Success(info);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant backup failed for {User}.", currentUser.Username);
            return Result<TenantBackupInfo>.Failure("The backup could not be created. Please try again.", 500);
        }
    }
}
