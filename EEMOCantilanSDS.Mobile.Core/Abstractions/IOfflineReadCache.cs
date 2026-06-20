namespace EEMOCantilanSDS.Mobile.Abstractions;

/// <summary>
/// On-device key→value cache of the last successful API read for each endpoint, so collection lists are
/// still visible after the app is opened without signal. Keyed by endpoint + parameters; values are the
/// response DTOs. Read-only display data — writes still go through the offline queue.
/// </summary>
public interface IOfflineReadCache
{
    /// <summary>Stores the latest value for <paramref name="key"/> (overwrites any previous value).</summary>
    Task SetAsync<T>(string key, T value);

    /// <summary>Returns the last cached value for <paramref name="key"/>, or <c>default</c> if none.</summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>Removes every cached entry (e.g. on login/logout to prevent cross-collector leakage).</summary>
    Task ClearAsync();

    /// <summary>Removes every entry whose key starts with any of the given prefixes (targeted invalidation).</summary>
    Task RemoveByPrefixAsync(params string[] prefixes);
}
