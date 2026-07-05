using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetRestoreRuns;

/// <summary>Recent restore workflow runs for the Head-only Backups page (newest first).</summary>
public record GetRestoreRunsQuery(int Count = 10) : IRequest<Result<IReadOnlyList<BackupRunDto>>>;
