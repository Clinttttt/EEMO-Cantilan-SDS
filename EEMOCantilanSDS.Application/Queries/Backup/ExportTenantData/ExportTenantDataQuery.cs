using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.ExportTenantData;

/// <summary>Downloadable data export for the authenticated caller's own municipality (tenant-scoped).</summary>
public record ExportTenantDataQuery : IRequest<Result<BackupArtifact>>;
