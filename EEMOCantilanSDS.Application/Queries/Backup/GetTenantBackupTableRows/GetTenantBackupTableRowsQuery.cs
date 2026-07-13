using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackupTableRows;

/// <summary>Inspect the actual records of one table inside one of the caller's OWN stored backups.</summary>
public record GetTenantBackupTableRowsQuery(Guid Id, string Table) : IRequest<Result<TenantBackupTableRowsDto>>;
