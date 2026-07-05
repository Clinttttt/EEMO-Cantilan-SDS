using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetBackupRunDetail;

public class GetBackupRunDetailQueryHandler(IBackupService backupService)
    : IRequestHandler<GetBackupRunDetailQuery, Result<BackupRunDetailDto>>
{
    public async Task<Result<BackupRunDetailDto>> Handle(GetBackupRunDetailQuery request, CancellationToken cancellationToken)
        => await backupService.GetRunDetailAsync(request.RunId, cancellationToken);
}
