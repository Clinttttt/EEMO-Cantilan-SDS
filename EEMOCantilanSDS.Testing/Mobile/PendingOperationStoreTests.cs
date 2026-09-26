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

    private static PendingOperation IssuedWcfOp() => new()
    {
        ClientOperationId = Guid.NewGuid(),
        Kind = OfflineOperationKind.WcfCollection,
        PayloadVersion = 1,
        BusinessDate = new DateOnly(2026, 9, 27),
        UtilityBillId = Guid.NewGuid(),
        ReceivedAmount = 125m,
        WaterSourceVersion = 9,
        AccountableDocumentId = Guid.NewGuid(),
        DocumentNumber = "CT-004126",
        IssuedAtUtc = new DateTime(2026, 9, 27, 3, 10, 0, DateTimeKind.Utc),
        OwnerKey = "collector-A",
        Title = "Maria Santos",
        FacilityLabel = "NPM",
        Amount = 125m
    };

    [Fact]
    public void ToDto_CarriesIsAbsent_WithoutClobberingIsPaidOrStall()
    {
        var stallId = Guid.NewGuid();
        var absent = new PendingOperation
        {
            Kind = OfflineOperationKind.NpmDaily,
            BusinessDate = new DateOnly(2026, 6, 5),
            StallId = stallId,
            IsPaid = false,
            IsAbsent = true,
        };

        var dto = absent.ToDto();

        Assert.True(dto.IsAbsent == true);   // positional mapping correct — lands on IsAbsent, not another field
        Assert.True(dto.IsPaid == false);
        Assert.Equal(stallId, dto.StallId);

        // A normal paid op stays non-absent (unchanged behavior).
        Assert.True(NpmOp().ToDto().IsAbsent != true);
    }

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

    [Fact]
    public async Task Physically_issued_ticket_and_operation_survive_a_fresh_store_instance()
    {
        var operation = IssuedWcfOp();
        var writer = new PendingOperationStore(_dir);
        await writer.AddIssuedDocumentOperationAsync(operation);

        var reader = new PendingOperationStore(_dir);
        var persisted = Assert.Single(await reader.GetAllAsync());

        Assert.Equal(operation.ClientOperationId, persisted.ClientOperationId);
        Assert.Equal(operation.AccountableDocumentId, persisted.AccountableDocumentId);
        Assert.Equal(operation.DocumentNumber, persisted.DocumentNumber);
        Assert.Equal(operation.UtilityBillId, persisted.UtilityBillId);
        Assert.Equal(operation.ReceivedAmount, persisted.ReceivedAmount);
        Assert.Equal(operation.BusinessDate, persisted.BusinessDate);
        Assert.Equal(IssuedDocumentLocalState.IssuedLocallyPendingSync, persisted.IssuedDocumentState);
        Assert.False(reader.HasStorageFault);
    }

    [Fact]
    public async Task Corrupt_queue_keeps_a_restart_persistent_issue_block_and_preserves_evidence()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "pending-operations.json"), "{not valid json");

        var first = new PendingOperationStore(_dir);
        Assert.Empty(await first.GetAllAsync());
        Assert.True(first.HasStorageFault);
        Assert.Single(Directory.GetFiles(_dir, "pending-operations.json.unreadable-*"));

        var afterRestart = new PendingOperationStore(_dir);
        Assert.Empty(await afterRestart.GetAllAsync());
        Assert.True(afterRestart.HasStorageFault);
        await Assert.ThrowsAsync<IOException>(() => afterRestart.AddIssuedDocumentOperationAsync(IssuedWcfOp()));
        Assert.Single(Directory.GetFiles(_dir, "pending-operations.json.unreadable-*"));
    }
}
