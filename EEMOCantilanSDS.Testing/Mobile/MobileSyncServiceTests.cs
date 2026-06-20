using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Moq;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class MobileSyncServiceTests
{
    private const string CollectorA = "collector-A";

    private static PendingOperation NpmOp(string? or = null, string? owner = CollectorA) => new()
    {
        Kind = OfflineOperationKind.NpmDaily,
        BusinessDate = new DateOnly(2026, 6, 5),
        StallId = Guid.NewGuid(),
        IsPaid = true,
        ORNumber = or,
        OwnerKey = owner,
        FacilityLabel = "NPM",
        Title = "Payor",
        Amount = 30m
    };

    private static MobileSyncService Sut(IPendingOperationStore store, IMobileApiClient api, bool online, string? collectorKey = CollectorA) =>
        new(store, api, new FakeConnectivityMonitor(online), new FakeCurrentCollectorProvider { CollectorKey = collectorKey });

    // Returns a Synced result for every operation in the dispatched command.
    private static Mock<IMobileApiClient> ApiAllSynced(List<SyncOfflineCollectionsCommand>? captured = null)
    {
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()))
            .ReturnsAsync((SyncOfflineCollectionsCommand cmd) =>
            {
                captured?.Add(cmd);
                var results = cmd.Operations
                    .Select(o => new SyncOperationResultDto(o.ClientOperationId, SyncResultStatus.Synced, null))
                    .ToList();
                return Result<SyncOfflineCollectionsResultDto>.Success(
                    new SyncOfflineCollectionsResultDto(results.Count, 0, 0, results));
            });
        return api;
    }

    [Fact]
    public async Task SyncNow_when_offline_is_a_no_op_and_never_calls_the_api()
    {
        var store = new FakePendingOperationStore(NpmOp());
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        var sut = Sut(store, api.Object, online: false);

        var summary = await sut.SyncNowAsync();

        Assert.Equal(0, summary.Total);
        Assert.Single(store.Snapshot);
        Assert.Equal(PendingLocalStatus.Pending, store.Snapshot[0].LocalStatus);
        api.Verify(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()), Times.Never);
    }

    [Fact]
    public async Task SyncNow_success_removes_synced_items_from_the_queue()
    {
        var store = new FakePendingOperationStore(NpmOp("OR-1"));
        var api = ApiAllSynced();
        var sut = Sut(store, api.Object, online: true);

        var summary = await sut.SyncNowAsync();

        Assert.Equal(1, summary.Synced);
        Assert.Empty(store.Snapshot);
        Assert.Equal(0, sut.PendingCount);
    }

    [Fact]
    public async Task SyncNow_rejected_keeps_item_with_message_and_no_retry()
    {
        var op = NpmOp("DUP");
        var store = new FakePendingOperationStore(op);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()))
            .ReturnsAsync(Result<SyncOfflineCollectionsResultDto>.Success(
                new SyncOfflineCollectionsResultDto(0, 1, 0, new List<SyncOperationResultDto>
                {
                    new(op.ClientOperationId, SyncResultStatus.Rejected, "OR number already exists.")
                })));
        var sut = Sut(store, api.Object, online: true);

        var summary = await sut.SyncNowAsync();

        Assert.Equal(1, summary.Rejected);
        var kept = Assert.Single(store.Snapshot);
        Assert.Equal(PendingLocalStatus.Rejected, kept.LocalStatus);
        Assert.Equal("OR number already exists.", kept.ResultMessage);

        api.Invocations.Clear();
        await sut.SyncNowAsync();
        api.Verify(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()), Times.Never);
    }

    [Fact]
    public async Task SyncNow_transient_failure_keeps_item_as_failed_for_retry()
    {
        var store = new FakePendingOperationStore(NpmOp());
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()))
            .ReturnsAsync(Result<SyncOfflineCollectionsResultDto>.Failure("Server error.", 500));
        var sut = Sut(store, api.Object, online: true);

        var summary = await sut.SyncNowAsync();

        Assert.Equal(1, summary.Failed);
        var kept = Assert.Single(store.Snapshot);
        Assert.Equal(PendingLocalStatus.Failed, kept.LocalStatus);
        Assert.Equal(1, sut.PendingCount);
    }

    [Fact]
    public async Task Failed_items_are_retried_and_can_then_succeed()
    {
        var store = new FakePendingOperationStore(NpmOp());
        var api = new Mock<IMobileApiClient>();
        var calls = 0;
        api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()))
            .ReturnsAsync((SyncOfflineCollectionsCommand cmd) =>
            {
                calls++;
                if (calls == 1)
                    return Result<SyncOfflineCollectionsResultDto>.Failure("Server error.", 500);

                var results = cmd.Operations
                    .Select(o => new SyncOperationResultDto(o.ClientOperationId, SyncResultStatus.Synced, null))
                    .ToList();
                return Result<SyncOfflineCollectionsResultDto>.Success(
                    new SyncOfflineCollectionsResultDto(results.Count, 0, 0, results));
            });
        var sut = Sut(store, api.Object, online: true);

        await sut.SyncNowAsync();
        Assert.Single(store.Snapshot);

        await sut.SyncNowAsync();
        Assert.Empty(store.Snapshot);
    }

    [Fact]
    public async Task Enqueue_while_offline_keeps_item_pending()
    {
        var store = new FakePendingOperationStore();
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        var sut = Sut(store, api.Object, online: false);

        await sut.EnqueueAsync(NpmOp());

        Assert.Single(store.Snapshot);
        Assert.Equal(PendingLocalStatus.Pending, store.Snapshot[0].LocalStatus);
        Assert.Equal(CollectorA, store.Snapshot[0].OwnerKey); // stamped with the capturing collector
        Assert.Equal(1, sut.PendingCount);
        api.Verify(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()), Times.Never);
    }

    [Fact]
    public async Task Dispatched_command_reuses_the_stored_client_operation_id_idempotency_key()
    {
        var op = NpmOp("OR-1");
        var store = new FakePendingOperationStore(op);
        var captured = new List<SyncOfflineCollectionsCommand>();
        var api = ApiAllSynced(captured);
        var sut = Sut(store, api.Object, online: true);

        await sut.SyncNowAsync();

        var sentOp = Assert.Single(Assert.Single(captured).Operations);
        Assert.Equal(op.ClientOperationId, sentOp.ClientOperationId);
    }

    [Fact]
    public async Task SyncNow_with_empty_queue_raises_no_change_event_and_calls_no_api()
    {
        var store = new FakePendingOperationStore();
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        var sut = Sut(store, api.Object, online: true);
        var changes = 0;
        sut.Changed += () => Interlocked.Increment(ref changes);

        var summary = await sut.SyncNowAsync();

        Assert.Equal(0, summary.Total);
        Assert.Equal(0, changes);
        api.Verify(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()), Times.Never);
    }

    [Fact]
    public async Task SyncNow_with_only_rejected_items_raises_no_change_event()
    {
        var rejected = NpmOp("DUP");
        rejected.LocalStatus = PendingLocalStatus.Rejected;
        var store = new FakePendingOperationStore(rejected);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        var sut = Sut(store, api.Object, online: true);
        var changes = 0;
        sut.Changed += () => Interlocked.Increment(ref changes);

        await sut.SyncNowAsync();

        Assert.Equal(0, changes);
        api.Verify(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()), Times.Never);
    }

    [Fact]
    public async Task Mixed_batch_syncs_good_items_and_keeps_rejected()
    {
        var ok = NpmOp("OR-1");
        var bad = NpmOp("DUP");
        var store = new FakePendingOperationStore(ok, bad);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()))
            .ReturnsAsync((SyncOfflineCollectionsCommand cmd) =>
            {
                var results = cmd.Operations.Select(o =>
                    o.ClientOperationId == bad.ClientOperationId
                        ? new SyncOperationResultDto(o.ClientOperationId, SyncResultStatus.Rejected, "Duplicate OR.")
                        : new SyncOperationResultDto(o.ClientOperationId, SyncResultStatus.Synced, null)).ToList();
                return Result<SyncOfflineCollectionsResultDto>.Success(
                    new SyncOfflineCollectionsResultDto(
                        results.Count(r => r.Status == SyncResultStatus.Synced),
                        results.Count(r => r.Status == SyncResultStatus.Rejected),
                        0, results));
            });
        var sut = Sut(store, api.Object, online: true);

        var summary = await sut.SyncNowAsync();

        Assert.Equal(1, summary.Synced);
        Assert.Equal(1, summary.Rejected);
        var remaining = Assert.Single(store.Snapshot);
        Assert.Equal(bad.ClientOperationId, remaining.ClientOperationId);
        Assert.Equal(PendingLocalStatus.Rejected, remaining.LocalStatus);
    }

    // ── Collector-scoped queue ownership (GPT's acceptance test) ────────────
    [Fact]
    public async Task A_queued_op_is_synced_only_by_its_owning_collector()
    {
        var opA = NpmOp("OR-A", owner: "collector-A");
        var store = new FakePendingOperationStore(opA);
        var captured = new List<SyncOfflineCollectionsCommand>();
        var api = ApiAllSynced(captured);

        // Collector B signs in on the same device and syncs → must send NONE of A's queue.
        var sutB = Sut(store, api.Object, online: true, collectorKey: "collector-B");
        var summaryB = await sutB.SyncNowAsync();

        Assert.Equal(0, summaryB.Total);
        Assert.Empty(captured);                // the network was never even called for A's op
        Assert.Single(store.Snapshot);         // A's op is still safely queued

        // Collector A signs back in → A's original op syncs exactly once.
        var sutA = Sut(store, api.Object, online: true, collectorKey: "collector-A");
        var summaryA = await sutA.SyncNowAsync();

        Assert.Equal(1, summaryA.Synced);
        Assert.Empty(store.Snapshot);
        var sentOp = Assert.Single(Assert.Single(captured).Operations);
        Assert.Equal(opA.ClientOperationId, sentOp.ClientOperationId);
    }

    [Fact]
    public async Task GetAll_and_count_only_include_the_current_collectors_ops()
    {
        var store = new FakePendingOperationStore(
            NpmOp("OR-A", owner: "collector-A"),
            NpmOp("OR-B", owner: "collector-B"));
        var sut = Sut(store, new Mock<IMobileApiClient>().Object, online: false, collectorKey: "collector-A");

        await sut.InitializeAsync();
        var mine = await sut.GetAllAsync();

        Assert.Single(mine);
        Assert.Equal("OR-A", mine[0].ORNumber);
        Assert.Equal(1, sut.PendingCount);
    }

    [Fact]
    public async Task Orphaned_op_is_visible_for_recovery_but_never_synced()
    {
        var orphan = NpmOp("OR-X", owner: null); // e.g. a row captured before OwnerKey existed
        var store = new FakePendingOperationStore(orphan);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict); // sync must never run for an orphan
        var sut = Sut(store, api.Object, online: true, collectorKey: "collector-A");

        await sut.InitializeAsync();

        // Visible + counted (recoverable) → never silently hidden/lost...
        Assert.Single(await sut.GetAllAsync());
        Assert.Equal(1, sut.PendingCount);

        // ...but never synced (can't be attributed to whoever happens to be signed in).
        var summary = await sut.SyncNowAsync();
        Assert.Equal(0, summary.Total);
        Assert.Single(store.Snapshot);
        api.Verify(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>()), Times.Never);
    }
}
