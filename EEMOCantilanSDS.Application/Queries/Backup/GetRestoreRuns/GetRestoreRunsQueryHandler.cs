using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetRestoreRuns;

public class GetRestoreRunsQueryHandler(IBackupService backupService)
    : IRequestHandler<GetRestoreRunsQuery, Result<IReadOnlyList<BackupRunDto>>>
{
    public async Task<Result<IReadOnlyList<BackupRunDto>>> Handle(GetRestoreRunsQuery request, CancellationToken cancellationToken)
        => await backupService.GetRecentRestoreRunsAsync(request.Count, cancellationToken);
}
