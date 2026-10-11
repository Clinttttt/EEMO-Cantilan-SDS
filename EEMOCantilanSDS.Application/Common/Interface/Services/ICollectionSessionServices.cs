using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

public interface ICollectionSessionSources
{
    Task<IReadOnlyList<CollectionSourceSearchResult>> SearchSourcesAsync(string? search, CancellationToken ct) => Task.FromResult<IReadOnlyList<CollectionSourceSearchResult>>([]);
    Task<CollectionSessionDiscovery> DiscoverNativeAsync(CollectionSourceIdentity? identity, DateOnly date, CancellationToken ct) => throw new NotSupportedException();
    Task<bool> SourceExistsAsync(CollectionSourceIdentity identity, CancellationToken ct) => Task.FromResult(false);
    /// <summary>A bounded, filterable page of the registered sources this collector may collect from. Read-only.</summary>
    Task<CollectionSourcePage> BrowseSourcesAsync(string? category, string? search, int skip, int take, CancellationToken ct) => Task.FromResult(new CollectionSourcePage([], 0, []));
    Task<CollectionSessionDiscovery> DiscoverAsync(Guid? payorId, DateOnly date, CancellationToken ct);
    Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct);
    Task<CollectionSessionCollection> PostAsync(CollectionSessionIntent session, CollectionSessionItemIntent item,
        Guid childOperationId, CancellationToken ct);
}
public interface ICollectionSessionStore
{
    Task<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto>> SearchPayorsAsync(Guid tenantId, string search, CancellationToken ct);
    Task<bool> IsActiveCollectorAsync(Guid tenantId, Guid collectorId, CancellationToken ct);
    Task<bool> PayorExistsAsync(Guid tenantId, Guid payorId, CancellationToken ct);
    Task<MobileCollectionSession?> FindAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task ExecuteAtomicallyAsync(Func<Task> action, CancellationToken ct);
    Task SaveAsync(MobileCollectionSession session, CancellationToken ct);
    Task<IReadOnlyList<CollectionSessionCollection>> RefreshDispositionAsync(
        IReadOnlyList<CollectionSessionCollection> collections, CancellationToken ct);
}
