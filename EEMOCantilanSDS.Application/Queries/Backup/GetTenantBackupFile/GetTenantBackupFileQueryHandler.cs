using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackupFile;

public class GetTenantBackupFileQueryHandler(ITenantBackupRepository repository)
    : IRequestHandler<GetTenantBackupFileQuery, Result<BackupArtifact>>
{
    public async Task<Result<BackupArtifact>> Handle(GetTenantBackupFileQuery request, CancellationToken ct)
    {
        var file = await repository.GetFileAsync(request.Id, ct);
        if (file is null)
            return Result<BackupArtifact>.NotFound();

        var stamp = file.Value.Info.CreatedAtUtc.ToString("yyyyMMdd-HHmmss");
        var fileName = $"stalltrack-backup-{stamp}.json";
        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, file.Value.Bytes, "application/json"));
    }
}
