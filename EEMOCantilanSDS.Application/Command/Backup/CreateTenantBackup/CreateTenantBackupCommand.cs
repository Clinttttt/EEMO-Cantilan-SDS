using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Backup.CreateTenantBackup;

/// <summary>
/// Head-triggered capture of a stored backup of the caller's OWN municipality. Non-destructive; the
/// repository stamps/scopes it to the caller's tenant and enforces retention (keep last N).
/// </summary>
public record CreateTenantBackupCommand(string? Note = null) : IRequest<Result<TenantBackupInfo>>;
