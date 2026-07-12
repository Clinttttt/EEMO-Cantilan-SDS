using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantRestoreSnapshot;

/// <summary>Downloads a round-trippable snapshot of the caller's OWN municipality (the file a restore accepts).</summary>
public record GetTenantRestoreSnapshotQuery : IRequest<Result<BackupArtifact>>;
