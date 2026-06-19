using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class SyncRepository(AppDbContext context) : ISyncRepository
{
    public async Task<bool> IsOperationProcessedAsync(Guid clientOperationId, CancellationToken cancellationToken = default)
    {
        // A client operation id is globally unique and stamped on exactly one collection record, so
        // a hit in any table means it was already synced. Bypass soft-delete filters so a synced-then-
        // voided record is still treated as processed (never re-created from the queue).
        return await context.DailyCollections.IgnoreQueryFilters()
                   .AnyAsync(x => x.ClientOperationId == clientOperationId, cancellationToken)
            || await context.PaymentRecords.IgnoreQueryFilters()
                   .AnyAsync(x => x.ClientOperationId == clientOperationId, cancellationToken)
            || await context.SlaughterTransactions.IgnoreQueryFilters()
                   .AnyAsync(x => x.ClientOperationId == clientOperationId, cancellationToken)
            || await context.TrmTrips.IgnoreQueryFilters()
                   .AnyAsync(x => x.ClientOperationId == clientOperationId, cancellationToken)
            || await context.TpmAttendances.IgnoreQueryFilters()
                   .AnyAsync(x => x.ClientOperationId == clientOperationId, cancellationToken);
    }
}
