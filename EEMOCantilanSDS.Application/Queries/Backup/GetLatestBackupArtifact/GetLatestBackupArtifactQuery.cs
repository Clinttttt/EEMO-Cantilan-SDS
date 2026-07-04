using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetLatestBackupArtifact;

/// <summary>Download the artifact of the newest successful backup run (streamed back through the API).</summary>
public record GetLatestBackupArtifactQuery : IRequest<Result<BackupArtifact>>;
