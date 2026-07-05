using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetBackupRunDetail;

/// <summary>Detailed view (run summary + step timeline) of a single backup workflow run for the Head-only Backups page.</summary>
public record GetBackupRunDetailQuery(long RunId) : IRequest<Result<BackupRunDetailDto>>;
