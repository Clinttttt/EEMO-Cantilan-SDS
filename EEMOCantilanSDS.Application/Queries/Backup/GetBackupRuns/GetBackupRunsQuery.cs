using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetBackupRuns;

/// <summary>Recent backup workflow runs for the Head-only Backups page (newest first).</summary>
public record GetBackupRunsQuery(int Count = 10) : IRequest<Result<IReadOnlyList<BackupRunDto>>>;
