using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class PendingOperationStoreTests : IDisposable
{
    private readonly string _dir;

    public PendingOperationStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eemo-pending-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static PendingOperation NpmOp(string? or = null) => new()
    {
        Kind = OfflineOperationKind.NpmDaily,
        BusinessDate = new DateOnly(2026, 6, 5),
        StallId = Guid.NewGuid(),
        IsPaid = true,
        ORNumber = or,
        FacilityLabel = "NPM",
        Title = "Payor",
        Amount = 30m
    };

    [Fact]
    public async Task Add_then_GetAll_returns_the_item()
    {
        var store = new PendingOperationStore(_dir);
        var op = NpmOp("OR-1");

        await store.AddAsync(op);

        var all = await store.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(op.ClientOperationId, all[0].ClientOperationId);
    }

    [Fact]
    public async Task Update_replaces_status_and_message()
    {
        var store = new PendingOperationStore(_dir);
        var op = NpmOp();
        await store.AddAsync(op);

        op.LocalStatus = PendingLocalStatus.Rejected;
        op.ResultMessage = "OR number already exists.";
        await store.UpdateAsync(op);

        var all = await store.GetAllAsync();
        Assert.Equal(PendingLocalStatus.Rejected, all[0].LocalStatus);
        Assert.Equal("OR number already exists.", all[0].ResultMessage);
    }

    [Fact]
    public async Task Remove_deletes_the_item()
    {
        var store = new PendingOperationStore(_dir);
        var op = NpmOp();
        await store.AddAsync(op);

        await store.RemoveAsync(op.ClientOperationId);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Items_persist_to_disk_and_reload_in_a_fresh_store()
    {
        var op = NpmOp("OR-9");

        // First store instance writes the file.
        var writer = new PendingOperationStore(_dir);
        await writer.AddAsync(op);

        // A brand-new instance (cold start) must read it back from disk.
        var reader = new PendingOperationStore(_dir);
        var all = await reader.GetAllAsync();

        Assert.Single(all);
        Assert.Equal(op.ClientOperationId, all[0].ClientOperationId);
        Assert.Equal("OR-9", all[0].ORNumber);
        Assert.True(all[0].IsPaid);
    }

    [Fact]
    public async Task GetAll_orders_newest_first()
    {
        var store = new PendingOperationStore(_dir);
        var older = NpmOp();
        older.CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = NpmOp();
        newer.CreatedAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

        await store.AddAsync(older);
        await store.AddAsync(newer);

        var all = await store.GetAllAsync();
        Assert.Equal(newer.ClientOperationId, all[0].ClientOperationId);
        Assert.Equal(older.ClientOperationId, all[1].ClientOperationId);
    }
}
