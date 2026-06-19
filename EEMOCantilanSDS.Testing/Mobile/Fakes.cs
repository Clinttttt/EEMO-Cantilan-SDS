using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.UnitTest.Mobile;

/// <summary>In-memory <see cref="IPendingOperationStore"/> for sync-service tests (no disk, no MAUI).</summary>
public sealed class FakePendingOperationStore : IPendingOperationStore
{
    private readonly List<PendingOperation> _items = new();

    public FakePendingOperationStore(params PendingOperation[] seed) => _items.AddRange(seed);

    public Task<IReadOnlyList<PendingOperation>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<PendingOperation>>(
            _items.OrderByDescending(o => o.CreatedAt).ToList());

    public Task AddAsync(PendingOperation operation)
    {
        _items.Add(operation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PendingOperation operation)
    {
        var index = _items.FindIndex(o => o.ClientOperationId == operation.ClientOperationId);
        if (index >= 0)
            _items[index] = operation;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid clientOperationId)
    {
        _items.RemoveAll(o => o.ClientOperationId == clientOperationId);
        return Task.CompletedTask;
    }

    // Test inspection helpers
    public IReadOnlyList<PendingOperation> Snapshot => _items.ToList();
    public int Count => _items.Count;
}

/// <summary>Controllable <see cref="IConnectivityMonitor"/>; flip <see cref="IsOnline"/> and raise restore.</summary>
public sealed class FakeConnectivityMonitor : IConnectivityMonitor
{
    public FakeConnectivityMonitor(bool online = true) => IsOnline = online;

    public bool IsOnline { get; set; }

    public event Action? ConnectivityRestored;

    public void RaiseRestored() => ConnectivityRestored?.Invoke();
}
