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
            // An unreadable queue file is not an empty queue. Keep it — renamed, so a fresh file can be written —
            // and record that captures may be sitting in it, rather than telling the collector nothing is waiting
            // to sync when the truth is unknown.
            _cache = new List<PendingOperation>();
            PreserveUnreadableFile();
        }

        return _cache;
    }

    /// <summary>
    /// True when a queue file could not be read or written. The collector is told, because "nothing waiting to
    /// sync" is a statement about their money and must not be guessed.
    /// </summary>
    public bool HasStorageFault { get; private set; }

    private void PreserveUnreadableFile()
    {
        HasStorageFault = true;
        try
        {
            if (!File.Exists(_filePath)) return;
            var kept = _filePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(_filePath, kept, overwrite: true);
        }
        catch
        {
            // Nothing more can be done here; the fault is already recorded.
        }
    }

    private async Task SaveUnsafeAsync(List<PendingOperation> items)
    {
        _cache = items;
        try
        {
            var json = JsonSerializer.Serialize(items, JsonOptions);
            await JsonOfflineReadCache.WriteDurableAsync(_filePath, json);
            HasStorageFault = false;
        }
        catch
        {
            // The capture is in memory for this run, but it is NOT safe on the device — which is what the queue
            // promises. Recorded so the review sheet can say so instead of showing a clean queue.
            HasStorageFault = true;
        }
    }
}
