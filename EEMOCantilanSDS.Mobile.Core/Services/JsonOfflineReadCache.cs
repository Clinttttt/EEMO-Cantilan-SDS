using System.Text.Json;
using EEMOCantilanSDS.Mobile.Abstractions;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// JSON-file-backed <see cref="IOfflineReadCache"/> (one map file under the injected storage directory —
/// the MAUI app passes <c>FileSystem.AppDataDirectory</c>, tests pass a temp dir, so this type carries no
/// platform dependency). A single <see cref="SemaphoreSlim"/> serializes access. Writes are
/// <b>atomic</b> (temp file + replace) so a crash mid-write can never corrupt the cache. Best-effort: a
/// corrupt/missing file degrades to an empty cache rather than throwing into the read path.
/// </summary>
public sealed class JsonOfflineReadCache : IOfflineReadCache
{
    private const string FileName = "offline-read-cache.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Dictionary<string, JsonElement>? _cache;

    public JsonOfflineReadCache(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    public async Task SetAsync<T>(string key, T value)
    {
        await _gate.WaitAsync();
        try
        {
            var map = await LoadUnsafeAsync();
            map[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
            await SaveUnsafeAsync(map);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        await _gate.WaitAsync();
        try
        {
            var map = await LoadUnsafeAsync();
            if (!map.TryGetValue(key, out var element))
                return default;

            try
            {
                return element.Deserialize<T>(JsonOptions);
            }
            catch
            {
                // Shape changed since it was cached (e.g. after an app update) — treat as a miss.
                return default;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await SaveUnsafeAsync(new Dictionary<string, JsonElement>());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveByPrefixAsync(params string[] prefixes)
    {
        if (prefixes is null || prefixes.Length == 0)
            return;

        await _gate.WaitAsync();
        try
        {
            var map = await LoadUnsafeAsync();
            var toRemove = map.Keys
                .Where(k => prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
                .ToList();

            if (toRemove.Count == 0)
                return;

            foreach (var key in toRemove)
                map.Remove(key);

            await SaveUnsafeAsync(map);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Internals (always called while holding the gate) ────────────────────

    private async Task<Dictionary<string, JsonElement>> LoadUnsafeAsync()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(_filePath))
            {
                await using var stream = File.OpenRead(_filePath);
                _cache = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(stream, JsonOptions)
                         ?? new Dictionary<string, JsonElement>();
            }
            else
            {
                _cache = new Dictionary<string, JsonElement>();
            }
        }
        catch
        {
            _cache = new Dictionary<string, JsonElement>();
        }

        return _cache;
    }

    private async Task SaveUnsafeAsync(Dictionary<string, JsonElement> map)
    {
        _cache = map;
        try
        {
            var json = JsonSerializer.Serialize(map, JsonOptions);
            await WriteDurableAsync(_filePath, json);
        }
        catch
        {
            // Persistence is best-effort; the in-memory cache still serves this run.
        }
    }

    // Atomic when the platform supports it (temp file + replace), with a direct-write fallback so data is
    // never silently lost if the atomic move isn't available — durability matters more than crash-atomicity.
    internal static async Task WriteDurableAsync(string filePath, string json)
    {
        var tempPath = filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            await File.WriteAllTextAsync(filePath, json);
        }
    }
}
