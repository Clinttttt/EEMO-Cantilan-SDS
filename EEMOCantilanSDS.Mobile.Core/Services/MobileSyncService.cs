using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Owns the offline-collection queue lifecycle on the device: enqueue a capture, then replay queued
/// items through <c>POST /api/Mobile/sync</c> when connectivity allows and apply the per-item outcome.
///
/// <para>Sync triggers: connectivity-restored (auto, via <see cref="IConnectivityMonitor"/>), immediately
/// after each capture (best-effort), and manual ("Sync now"). The server write is idempotent by
/// <see cref="PendingOperation.ClientOperationId"/>, so retries are safe. Best-effort throughout —
/// failures never throw into the capture path; they leave items queued for the next attempt.</para>
///
/// <para><see cref="Changed"/> is raised off the UI thread; consumers marshal onto it (InvokeAsync).</para>
///
/// <para>This type is deliberately free of MAUI dependencies (connectivity + storage are injected) so it
/// can be unit-tested.</para>
/// </summary>
public sealed class MobileSyncService
{
    /// <summary>Server caps a sync batch at 200 operations (see SyncOfflineCollectionsCommandValidator).</summary>
    private const int MaxBatchSize = 200;

    /// <summary>
    /// How long a row waits after an unsuccessful attempt before it is sent again, by attempt number. Without
    /// this, one row the server keeps refusing made every later capture (and every reconnect) re-send the whole
    /// queue over a field connection. A collector who presses "Sync now" bypasses the wait entirely.
    /// The last value repeats, so a row that keeps failing settles into a five-minute retry rather than stopping.
    /// </summary>
    private static readonly TimeSpan[] RetryBackoff =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5)
    };

    /// <summary>How often the device retries by itself while rows are still waiting and it is online.</summary>
    private static readonly TimeSpan AutoRetryInterval = TimeSpan.FromMinutes(1);

    private readonly IPendingOperationStore _store;
    private readonly IMobileApiClient _api;
    private readonly IConnectivityMonitor _connectivity;
    private readonly ICurrentCollectorProvider _collector;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private static readonly TimeSpan ConnectivityDebounce = TimeSpan.FromSeconds(3);
    private readonly object _debounceLock = new();
    private DateTime _lastConnectivityTriggerUtc = DateTime.MinValue;

    public MobileSyncService(
        IPendingOperationStore store,
        IMobileApiClient api,
        IConnectivityMonitor connectivity,
        ICurrentCollectorProvider collector)
    {
        _store = store;
        _api = api;
        _connectivity = connectivity;
        _collector = collector;
        _connectivity.ConnectivityRestored += OnConnectivityRestored;
    }

    // An op belongs to the current collector only when someone is signed in AND the keys match. This is
    // what stops a different collector on the same device from syncing (and mis-attributing) A's captures.
    private bool IsOwnedByCurrent(PendingOperation op) =>
        _collector.CollectorKey is { } key && op.OwnerKey == key;

    // Visible (and discardable) to the current collector = their own ops, plus any orphaned (owner-less)
    // rows so they can never be silently hidden/lost. Orphans are still excluded from sync (see
    // IsOwnedByCurrent) so they can't be mis-attributed to whoever happens to be signed in.
    private bool IsVisibleToCurrent(PendingOperation op) =>
        op.OwnerKey is null || IsOwnedByCurrent(op);

    /// <summary>Raised (off the UI thread) whenever the queue or sync state changes.</summary>
    public event Action? Changed;

    /// <summary>Number of queued rows still needing attention (Pending + Failed + Rejected).</summary>
    public int PendingCount { get; private set; }

    /// <summary>True while a sync round-trip is in flight.</summary>
    public bool IsSyncing { get; private set; }

    /// <summary>True when the device currently has full internet access.</summary>
    public bool IsOnline => _connectivity.IsOnline;

    /// <summary>Loads the current queue count (call once on a screen that shows the pending badge).</summary>
    public async Task InitializeAsync()
    {
        await RefreshCountAsync();
        NotifyChanged();

        // Anything still waiting is retried by the device itself from here on, so a queue does not sit until the
        // collector happens to reopen a screen or the network happens to flap.
        EnsureAutoRetry();
    }

    /// <summary>Returns the current collector's queued rows (newest first) for the review sheet. Includes
    /// owner-less (orphaned) rows so they are recoverable rather than hidden.</summary>
    public async Task<IReadOnlyList<PendingOperation>> GetAllAsync()
    {
        var all = await _store.GetAllAsync();
        return all.Where(IsVisibleToCurrent).ToList();
    }

    /// <summary>
    /// Queues a captured collection and kicks a best-effort background sync. Always succeeds locally —
    /// the collection is safe on the device the moment this returns.
    /// </summary>
    public async Task EnqueueAsync(PendingOperation operation)
    {
        operation.LocalStatus = PendingLocalStatus.Pending;
        operation.ResultMessage = null;
        operation.OwnerKey = _collector.CollectorKey; // tag the capturing collector
        await _store.AddAsync(operation);
        await RefreshCountAsync();
        NotifyChanged();

        // Fire-and-forget: if there is signal, push it immediately; otherwise it waits in the queue.
        _ = Task.Run(() => TrySyncInBackgroundAsync(force: true));
        EnsureAutoRetry();
    }

    /// <summary>Drops a queued row (e.g. a Rejected item the collector chooses to discard).</summary>
    public async Task DiscardAsync(Guid clientOperationId)
    {
        await _store.RemoveAsync(clientOperationId);
        await RefreshCountAsync();
        NotifyChanged();
    }

    /// <summary>
    /// Replays every retryable queued item (Pending + Failed) through the sync endpoint in batches and
    /// applies the outcome: Synced → removed, Rejected → kept + message (no auto-retry), Failed → kept for
    /// the next attempt. No-op when offline, already syncing, or the queue has nothing retryable.
    /// </summary>
    /// <param name="force">
    /// True for an action the collector took themselves ("Sync now", a fresh capture): every retryable row is
    /// sent regardless of its backoff. False for automatic attempts, which respect the per-row wait.
    /// </param>
    public async Task<SyncSummary> SyncNowAsync(bool force = true)
    {
        if (IsSyncing || !IsOnline)
            return SyncSummary.Empty;

        await _syncGate.WaitAsync().ConfigureAwait(false);
        var started = false;
        try
        {
            // Nothing retryable for THIS collector → return WITHOUT flipping state or notifying.
            var initial = await _store.GetAllAsync().ConfigureAwait(false);
            if (!initial.Any(o => IsRetryable(o, force)) || !IsOnline)
                return SyncSummary.Empty;

            started = true;
            IsSyncing = true;
            NotifyChanged();

            var summary = SyncSummary.Empty;

            while (true)
            {
                var all = await _store.GetAllAsync().ConfigureAwait(false);
                var batch = all
                    .Where(o => IsRetryable(o, force))
                    .OrderBy(o => o.CreatedAt)
                    .Take(MaxBatchSize)
                    .ToList();

                if (batch.Count == 0 || !IsOnline)
                    break;

                SyncOfflineCollectionsResultDto? value;
                try
                {
                    var command = new SyncOfflineCollectionsCommand(batch.Select(o => o.ToDto()).ToList());
                    var result = await _api.SyncOfflineCollectionsAsync(command).ConfigureAwait(false);
                    value = result.IsSuccess ? result.Value : null;
                }
                catch
                {
                    // Network error / timeout → transient; keep the batch queued and stop this round.
                    value = null;
                }

                if (value is null)
                {
                    foreach (var op in batch)
                        await MarkFailedAsync(op, "Sync failed. Will retry when online.").ConfigureAwait(false);
                    summary = summary.Add(failed: batch.Count);
                    break;
                }

                var (synced, rejected, failed) = await ApplyResultsAsync(batch, value).ConfigureAwait(false);
                summary = summary.Add(synced, rejected, failed);

                // Stop if nothing in this batch moved forward — prevents a tight loop regardless of what
                // the server returns (e.g. an all-Failed batch, or a stale/duplicate response payload).
                if (synced + rejected == 0)
                    break;
            }

            return summary;
        }
        finally
        {
            if (started)
            {
                IsSyncing = false;
                await RefreshCountAsync().ConfigureAwait(false);
                NotifyChanged();
            }
            _syncGate.Release();
        }
    }

    private async Task<(int Synced, int Rejected, int Failed)> ApplyResultsAsync(
        IReadOnlyList<PendingOperation> batch,
        SyncOfflineCollectionsResultDto result)
    {
        var byId = result.Results.ToDictionary(r => r.ClientOperationId);
        var synced = 0;
        var rejected = 0;
        var failed = 0;

        foreach (var op in batch)
        {
            if (!byId.TryGetValue(op.ClientOperationId, out var itemResult))
            {
                // Server didn't report this item — treat as transient; retry next round.
                await MarkFailedAsync(op, "No result returned for this item.");
                failed++;
                continue;
            }

            switch (itemResult.Status)
            {
                case SyncResultStatus.Synced:
                    await _store.RemoveAsync(op.ClientOperationId);
                    synced++;
                    break;

                case SyncResultStatus.Rejected:
                    op.LocalStatus = PendingLocalStatus.Rejected;
                    op.ResultMessage = itemResult.Message;
                    await _store.UpdateAsync(op);
                    rejected++;
                    break;

                default: // Failed
                    await MarkFailedAsync(op, itemResult.Message ?? "Sync failed. Will retry when online.");
                    failed++;
                    break;
            }
        }

        return (synced, rejected, failed);
    }

    private async Task MarkFailedAsync(PendingOperation op, string message)
    {
        op.LocalStatus = PendingLocalStatus.Failed;
        op.ResultMessage = message;
        op.AttemptCount++;
        op.LastAttemptUtc = DateTime.UtcNow;
        await _store.UpdateAsync(op);
    }

    /// <summary>
    /// A row is retryable when it belongs to the signed-in collector, is not settled, and — for an automatic
    /// attempt — its backoff has elapsed. A collector-initiated sync ignores the backoff: they asked for it now.
    /// </summary>
    private bool IsRetryable(PendingOperation op, bool force)
    {
        if (!IsOwnedByCurrent(op)) return false;
        if (op.LocalStatus is not (PendingLocalStatus.Pending or PendingLocalStatus.Failed)) return false;
        if (force) return true;

        return NextAttemptDueUtc(op) <= DateTime.UtcNow;
    }

    /// <summary>When this row may next be sent automatically. Never attempted → now.</summary>
    private static DateTime NextAttemptDueUtc(PendingOperation op)
    {
        if (op.LastAttemptUtc is not { } last || op.AttemptCount <= 0)
            return DateTime.MinValue;

        var index = Math.Min(op.AttemptCount, RetryBackoff.Length - 1);
        return last + RetryBackoff[index];
    }

    private async Task RefreshCountAsync()
    {
        var all = await _store.GetAllAsync();
        PendingCount = all.Count(o => IsVisibleToCurrent(o) && o.LocalStatus != PendingLocalStatus.Synced);
    }

    private async Task TrySyncInBackgroundAsync(bool force = false)
    {
        try
        {
            await SyncNowAsync(force).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: a background sync must never surface to the capture path.
        }
    }

    // ── Automatic retry while rows are waiting ────────────────────────────────────────────────────────
    // Connectivity events cover a network that comes back, but not a server that was briefly unavailable while
    // the device stayed online — nothing would then retry until the collector captured again or reopened the
    // Records screen. This loop closes that gap and stops itself as soon as the queue is settled, so it costs
    // nothing on an idle device.

    private readonly object _loopLock = new();
    private Task? _retryLoop;

    private void EnsureAutoRetry()
    {
        lock (_loopLock)
        {
            if (_retryLoop is { IsCompleted: false })
                return;

            _retryLoop = Task.Run(AutoRetryLoopAsync);
        }
    }

    private async Task AutoRetryLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(AutoRetryInterval).ConfigureAwait(false);

                var all = await _store.GetAllAsync().ConfigureAwait(false);
                var waiting = all.Any(o => IsOwnedByCurrent(o)
                    && o.LocalStatus is PendingLocalStatus.Pending or PendingLocalStatus.Failed);

                // Nothing left to send: stop. A later capture (or app start) starts the loop again.
                if (!waiting)
                    return;

                if (IsOnline)
                    await TrySyncInBackgroundAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // The loop is a convenience; if it ever falls over, manual sync and connectivity events remain.
        }
    }

    private void OnConnectivityRestored()
    {
        // Android raises ConnectivityChanged in a burst when Wi-Fi reconnects (and again per transport
        // when both Wi-Fi and cellular are present). Debounce so one reconnect = one sync attempt, and
        // run it OFF the connectivity/UI thread so the event handler returns immediately.
        lock (_debounceLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastConnectivityTriggerUtc < ConnectivityDebounce)
                return;
            _lastConnectivityTriggerUtc = now;
        }

        _ = Task.Run(() => TrySyncInBackgroundAsync(force: true));
    }

    private void NotifyChanged() => Changed?.Invoke();
}

/// <summary>Aggregate outcome of a <see cref="MobileSyncService.SyncNowAsync"/> run (for a status toast).</summary>
public readonly record struct SyncSummary(int Synced, int Rejected, int Failed)
{
    public static SyncSummary Empty => new(0, 0, 0);

    public int Total => Synced + Rejected + Failed;

    public SyncSummary Add(int synced = 0, int rejected = 0, int failed = 0) =>
        new(Synced + synced, Rejected + rejected, Failed + failed);
}
