using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.UnitTest.Mobile;

/// <summary>Money on the device is stated apart from the server's report, and only what will still post counts as waiting.</summary>
public class PendingSyncSummaryTests
{
    private static PendingOperation Op(PendingLocalStatus status, decimal amount) => new() { LocalStatus = status, Amount = amount };

    [Fact]
    public void AQueuedLandingTicket_IsWaiting_WithItsAmount()
    {
        var summary = PendingSyncSummary.From([Op(PendingLocalStatus.Pending, 100m)]);
        Assert.Equal((1, 100m, 0), (summary.WaitingCount, summary.WaitingAmount, summary.AttentionCount));
    }

    [Fact]
    public void ARetryingCapture_IsStillWaiting_ButARefusedOrReconciliationRow_IsAttentionNotWaiting()
    {
        var summary = PendingSyncSummary.From(
        [
            Op(PendingLocalStatus.Failed, 30m),
            Op(PendingLocalStatus.Rejected, 50m),
            Op(PendingLocalStatus.ReconciliationRequired, 100m)
        ]);
        Assert.Equal((1, 30m, 2), (summary.WaitingCount, summary.WaitingAmount, summary.AttentionCount));
    }

    [Fact]
    public void ASyncedCapture_HasLeftTheWaitingFigure()
    {
        var summary = PendingSyncSummary.From([Op(PendingLocalStatus.Synced, 100m)]);
        Assert.Equal(PendingSyncSummary.None, summary);
    }
}
