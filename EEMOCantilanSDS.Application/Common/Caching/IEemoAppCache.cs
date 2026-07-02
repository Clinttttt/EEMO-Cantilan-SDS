namespace EEMOCantilanSDS.Application.Common.Caching;

public interface IEemoAppCache
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        IReadOnlyCollection<string> regions,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);
}
