using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetLatestBackupArtifact;

public class GetLatestBackupArtifactQueryHandler(IBackupService backupService)
    : IRequestHandler<GetLatestBackupArtifactQuery, Result<BackupArtifact>>
{
    public async Task<Result<BackupArtifact>> Handle(GetLatestBackupArtifactQuery request, CancellationToken cancellationToken)
        => await backupService.GetLatestArtifactAsync(cancellationToken);
}
