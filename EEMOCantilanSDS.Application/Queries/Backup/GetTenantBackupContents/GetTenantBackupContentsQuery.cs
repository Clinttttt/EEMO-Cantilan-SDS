using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackupContents;

/// <summary>Inspect the contents (per-table record counts) of one of the caller's OWN stored backups.</summary>
public record GetTenantBackupContentsQuery(Guid Id) : IRequest<Result<TenantBackupContentsDto>>;
