using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Backup.TriggerBackup;

public class TriggerBackupCommandHandler(IBackupService backupService)
    : IRequestHandler<TriggerBackupCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(TriggerBackupCommand request, CancellationToken cancellationToken)
        => await backupService.TriggerBackupAsync(cancellationToken);
}
