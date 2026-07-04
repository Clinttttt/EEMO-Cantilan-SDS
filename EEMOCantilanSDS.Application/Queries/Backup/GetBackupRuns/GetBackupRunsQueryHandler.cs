using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetBackupRuns;

public class GetBackupRunsQueryHandler(IBackupService backupService)
    : IRequestHandler<GetBackupRunsQuery, Result<IReadOnlyList<BackupRunDto>>>
{
    public async Task<Result<IReadOnlyList<BackupRunDto>>> Handle(GetBackupRunsQuery request, CancellationToken cancellationToken)
        => await backupService.GetRecentRunsAsync(request.Count, cancellationToken);
}
