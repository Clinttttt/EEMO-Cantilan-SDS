using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;

/// <summary>Live PostgreSQL health snapshot for the Head/Admin-only Settings page.</summary>
public record GetDatabaseHealthQuery : IRequest<Result<DatabaseHealthDto>>;
