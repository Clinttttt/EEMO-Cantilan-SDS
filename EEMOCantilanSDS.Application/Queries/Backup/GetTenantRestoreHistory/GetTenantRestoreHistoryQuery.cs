using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantRestoreHistory;

/// <summary>Recent restore events for the caller's OWN municipality (from the append-only audit log).</summary>
public record GetTenantRestoreHistoryQuery : IRequest<Result<IReadOnlyList<TenantRestoreEventDto>>>;
