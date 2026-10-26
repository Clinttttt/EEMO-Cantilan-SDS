using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;

/// <summary>
/// Live PostgreSQL health snapshot. <paramref name="PlatformWide"/> is true only for the platform operator (the API decides
/// from the token); an LGU Head gets just their own municipality's storage footprint, never the shared database's load.
/// </summary>
public record GetDatabaseHealthQuery(bool PlatformWide = false) : IRequest<Result<DatabaseHealthDto>>;
