using System.Text.Json;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// JSON-file-backed <see cref="IPendingOperationStore"/>. The storage directory is injected (the MAUI
/// app passes <c>FileSystem.AppDataDirectory</c>; tests pass a temp dir), so this type carries no
/// platform dependency. A single <see cref="SemaphoreSlim"/> serializes all reads/writes (the queue is
/// tiny — a handful of un-synced collections — so a flat file is simpler and dependency-free versus an
/// on-device database). Best-effort: a corrupt/missing file degrades to an empty queue rather than
/// throwing into the capture path.
/// </summary>
public sealed class PendingOperationStore : IPendingOperationStore
{
    private const string FileName = "pending-operations.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<PendingOperation>? _cache;

    public PendingOperationStore(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    public async Task<IReadOnlyList<PendingOperation>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var items = await LoadUnsafeAsync();
            return items
                .OrderByDescending(o => o.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(PendingOperation operation)
    {
        await _gate.WaitAsync();
        try
        {
            var items = await LoadUnsafeAsync();
            items.Add(operation);
            await SaveUnsafeAsync(items);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(PendingOperation operation)
    {
        await _gate.WaitAsync();
        try
        {
            var items = await LoadUnsafeAsync();
            var index = items.FindIndex(o => o.ClientOperationId == operation.ClientOperationId);
            if (index < 0)
                return;

            items[index] = operation;
            await SaveUnsafeAsync(items);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid clientOperationId)
    {
        await _gate.WaitAsync();
        try
        {
            var items = await LoadUnsafeAsync();
            if (items.RemoveAll(o => o.ClientOperationId == clientOperationId) > 0)
                await SaveUnsafeAsync(items);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Internals (always called while holding the gate) ────────────────────

    private async Task<List<PendingOperation>> LoadUnsafeAsync()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(_filePath))
            {
                await using var stream = File.OpenRead(_filePath);
                _cache = await JsonSerializer.DeserializeAsync<List<PendingOperation>>(stream, JsonOptions)
                         ?? new List<PendingOperation>();
            }
            else
            {
                _cache = new List<PendingOperation>();
            }
        }
        catch
        {
            // Corrupt/unreadable file → start clean rather than break the capture path.
            _cache = new List<PendingOperation>();
        }

        return _cache;
    }

    private async Task SaveUnsafeAsync(List<PendingOperation> items)
    {
        _cache = items;
        try
        {
            var json = JsonSerializer.Serialize(items, JsonOptions);
            await JsonOfflineReadCache.WriteDurableAsync(_filePath, json);
        }
        catch
        {
            // Persistence is best-effort; the in-memory cache still reflects the latest state for this run.
        }
    }
}
