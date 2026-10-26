using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;

public class GetDatabaseHealthQueryHandler(IDatabaseHealthRepository repository)
    : IRequestHandler<GetDatabaseHealthQuery, Result<DatabaseHealthDto>>
{
    public async Task<Result<DatabaseHealthDto>> Handle(GetDatabaseHealthQuery request, CancellationToken cancellationToken)
    {
        var health = await repository.GetHealthAsync(cancellationToken);
        if (request.PlatformWide)
        {
            return Result<DatabaseHealthDto>.Success(health);
        }

        // ST-11 #4: the shared server's size, load, deadlocks and uptime are the platform's, not one LGU's. A Head keeps
        // the view of their own storage; everything else is neutral (the panel renders null as "-", zero as zero).
        return Result<DatabaseHealthDto>.Success(new DatabaseHealthDto(
            DatabaseSizeBytes: 0, ActiveConnections: 0, IdleConnections: 0, TotalConnections: 0, MaxConnections: 0,
            BlockedQueries: 0, Deadlocks: 0, CacheHitRatioPct: 0, LongestQuerySeconds: null, UptimeSince: null,
            CollectedAt: health.CollectedAt,
            ProvisionedStorageBytes: health.ProvisionedStorageBytes,
            TenantSizeBytes: health.TenantSizeBytes));
    }
}
