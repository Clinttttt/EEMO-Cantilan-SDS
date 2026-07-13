using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackupFile;

/// <summary>Download one of the caller's OWN stored backups as its restore-ready JSON file.</summary>
public record GetTenantBackupFileQuery(Guid Id) : IRequest<Result<BackupArtifact>>;
