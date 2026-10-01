using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.Mobile.Presentation;

/// <summary>
/// What this device holds that the server has not yet posted: captures still pending or retrying after a transient failure.
/// It is stated beside the server's figures and never added to them — a collection is collected once the server posts it,
/// at which point it leaves the queue and appears in the report. Rows refused or held for office reconciliation are not
/// "waiting": they will not post on their own, so they are counted apart as needing attention.
/// </summary>
public sealed record PendingSyncSummary(int WaitingCount, decimal WaitingAmount, int AttentionCount)
{
    public static readonly PendingSyncSummary None = new(0, 0m, 0);

    public static PendingSyncSummary From(IEnumerable<PendingOperation> queue)
    {
        var rows = queue.ToList();
        var waiting = rows.Where(x => x.LocalStatus is PendingLocalStatus.Pending or PendingLocalStatus.Failed).ToList();
        return new PendingSyncSummary(
            waiting.Count,
            waiting.Sum(x => x.Amount),
            rows.Count(x => x.LocalStatus is PendingLocalStatus.Rejected or PendingLocalStatus.ReconciliationRequired));
    }
}
