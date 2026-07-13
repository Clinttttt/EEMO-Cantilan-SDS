using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackups;

/// <summary>The caller's OWN municipality's stored backups (metadata only), newest first.</summary>
public record GetTenantBackupsQuery : IRequest<Result<IReadOnlyList<TenantBackupInfo>>>;
