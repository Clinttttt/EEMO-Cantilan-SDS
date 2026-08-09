using Microsoft.AspNetCore.SignalR.Client;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// How long the collector app keeps trying to restore its realtime connection: for as long as the app is open.
///
/// <para>
/// SignalR's default policy retries at 0, 2, 10 and 30 seconds and then stops permanently. Collectors work on
/// mobile data in a coastal municipality, where a minute without signal is an ordinary morning rather than an
/// exceptional failure - so the app lost realtime on the first tunnel of the day and never regained it until it
/// was restarted. That is worse than having no realtime at all, because the screen goes on looking live.
/// </para>
///
/// <para>
/// The backoff is capped at thirty seconds rather than growing exponentially, so a collector coming back into
/// signal waits half a minute at most, not several. This lives in the shared project because the MAUI app targets
/// platform frameworks the test project cannot reference, and "it never gives up" is a claim that deserves a test.
/// </para>
/// </summary>
public sealed class HubRetryForeverPolicy : IRetryPolicy
{
    /// <summary>The longest a collector waits between attempts once the early retries are past.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => retryContext.PreviousRetryCount switch
    {
        0 => TimeSpan.Zero,
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        3 => TimeSpan.FromSeconds(10),
        // Never null. Returning null is how a policy says "stop trying", and that is the behaviour being removed.
        _ => MaxDelay
    };
}
