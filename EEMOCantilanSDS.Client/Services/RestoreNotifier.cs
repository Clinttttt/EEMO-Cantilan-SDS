using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Cross-page "restore complete" notifier. Registered SCOPED (one per Blazor circuit), so it survives
/// navigation between admin pages. When a restore is triggered, <see cref="Watch"/> starts a SINGLE,
/// bounded, self-terminating poll of the restore-runs feed; when the run settles it records the result
/// and raises <see cref="Changed"/>. A lightweight subscriber component (RestoreCompletionToast, mounted
/// once in the sidebar) shows the popup on whatever page the Head is on.
///
/// SAFETY: there is NO always-on timer. The poll runs ONLY while a restore is actively being watched
/// (started by an explicit Watch()), and stops on completion, after a ~22-minute cap, or on dispose.
/// This is the deliberate contrast to the earlier per-circuit sidebar timer that destabilized navigation.
/// </summary>
public sealed class RestoreNotifier(IBackupApiClient backupApi) : IDisposable
{
    private CancellationTokenSource? _cts;

    public bool Completed { get; private set; }
    public bool Success { get; private set; }
    public string WhenLabel { get; private set; } = string.Empty;

    /// <summary>Raised (off the poll loop) when a watched restore finishes.</summary>
    public event Action? Changed;

    /// <summary>Begin watching for the just-triggered restore to finish. Cancels any prior watch.</summary>
    public void Watch()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;
        Completed = false;
        _ = RunAsync(DateTime.UtcNow, cts.Token);
    }

    /// <summary>Dismiss the current notification so it does not re-show.</summary>
    public void Acknowledge() => Completed = false;

    private async Task RunAsync(DateTime sinceUtc, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
            var ticks = 0;
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (++ticks > 220) break;   // ~22 min hard cap

                var result = await backupApi.GetRecentRestoreRunsAsync();
                if (!result.IsSuccess || result.Value is null || result.Value.Count == 0) continue;

                var newest = result.Value[0];   // restore runs come newest-first
                if (!string.Equals(newest.Status, "completed", StringComparison.OrdinalIgnoreCase)) continue;
                if (newest.CreatedAt.ToUniversalTime() < sinceUtc.AddSeconds(-60)) continue;   // not our run

                Success = string.Equals(newest.Conclusion, "success", StringComparison.OrdinalIgnoreCase);
                WhenLabel = PhilippineTime.ToPhilippineTime(newest.CreatedAt).ToString("MMM d, yyyy · h:mm tt");
                Completed = true;
                Changed?.Invoke();
                break;
            }
        }
        catch (OperationCanceledException) { /* cancelled / disposed */ }
        catch { /* stop quietly — the Recent restores list still reflects the outcome */ }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
