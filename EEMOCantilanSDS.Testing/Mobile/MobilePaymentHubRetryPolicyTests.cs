using Microsoft.AspNetCore.SignalR.Client;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// How long the collector app keeps trying to restore its realtime connection.
///
/// <para>
/// SignalR's default policy retries at 0, 2, 10 and 30 seconds and then stops for good. Collectors work on mobile
/// data in a coastal municipality, where a minute without signal is an ordinary morning - so the app would lose
/// realtime on the first tunnel of the day and never regain it until restarted. That is worse than having no
/// realtime at all, because the screen goes on looking live.
/// </para>
/// </summary>
public class MobilePaymentHubRetryPolicyTests
{
    private static IRetryPolicy Policy() => new HubRetryForeverPolicy();

    [Fact]
    public void TheFirstAttemptIsImmediate_ThenBacksOff()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.Zero, policy.NextRetryDelay(Context(0)));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.NextRetryDelay(Context(1)));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextRetryDelay(Context(2)));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.NextRetryDelay(Context(3)));
    }

    [Fact]
    public void ItNeverGivesUp()
    {
        var policy = Policy();

        // A null delay is how a policy says "stop trying". It must never say that: an outage lasting an hour has
        // to be survivable, and a returning collector should find a working connection rather than a dead one.
        foreach (var attempt in new[] { 4, 10, 100, 5_000 })
        {
            var delay = policy.NextRetryDelay(Context(attempt));
            Assert.NotNull(delay);
            Assert.Equal(TimeSpan.FromSeconds(30), delay);
        }
    }

    [Fact]
    public void TheBackoffIsCapped_SoAReturningCollectorDoesNotWaitLong()
    {
        var policy = Policy();

        // An exponential policy would be minutes wide by this point. Thirty seconds bounds how long a collector
        // waits after coming back into signal.
        for (var attempt = 4; attempt < 50; attempt++)
            Assert.True(policy.NextRetryDelay(Context(attempt)) <= TimeSpan.FromSeconds(30));
    }

    private static RetryContext Context(long previousRetryCount) => new()
    {
        PreviousRetryCount = previousRetryCount,
        ElapsedTime = TimeSpan.FromSeconds(previousRetryCount * 30),
        RetryReason = new IOException("signal lost")
    };
}
