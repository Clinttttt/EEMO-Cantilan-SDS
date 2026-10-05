using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

public interface ICollectionSessionSources
{
    Task<CollectionSessionDiscovery> DiscoverAsync(Guid? payorId, DateOnly date, CancellationToken ct);
    Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct);
    Task<CollectionSessionCollection> PostAsync(CollectionSessionIntent session, CollectionSessionItemIntent item,
        Guid childOperationId, CancellationToken ct);
}
public interface ICollectionSessionStore
{
    Task<bool> IsActiveCollectorAsync(Guid tenantId, Guid collectorId, CancellationToken ct);
    Task<bool> PayorExistsAsync(Guid tenantId, Guid payorId, CancellationToken ct);
    Task<MobileCollectionSession?> FindAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task ExecuteAtomicallyAsync(Func<Task> action, CancellationToken ct);
    Task SaveAsync(MobileCollectionSession session, CancellationToken ct);
    Task<IReadOnlyList<CollectionSessionCollection>> RefreshDispositionAsync(
        IReadOnlyList<CollectionSessionCollection> collections, CancellationToken ct);
}
