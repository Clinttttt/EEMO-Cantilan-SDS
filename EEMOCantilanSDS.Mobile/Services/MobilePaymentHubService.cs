using EEMOCantilanSDS.Application.Common.Payments;
using Microsoft.AspNetCore.SignalR.Client;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Connects the collector app to the API's online-payment hub and raises <see cref="PaymentReceived"/>
/// when a payor pays online. Best-effort: connection failures never throw to callers. The access token
/// is pulled fresh from <see cref="MobileTokenStore"/> on every (re)connect so it survives token refresh.
///
/// <para>
/// SignalR does not queue messages for an absent client, so everything sent during a gap is lost. That makes the
/// gap itself the important event, not the reconnect: a payor can pay online while the collector is in a dead
/// spot, and if nothing re-reads the list afterwards the collector never sees it and collects the same money
/// again in person. <see cref="ConnectionRestored"/> exists for that - consumers re-fetch on it rather than
/// trusting that they missed nothing.
/// </para>
/// </summary>
public sealed class MobilePaymentHubService(MobileTokenStore tokenStore) : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised (off the UI thread) when an online payment is received.</summary>
    public event Action<OnlinePaymentNotification>? PaymentReceived;

    /// <summary>
    /// Raised after the connection comes back, because anything sent while it was down was never delivered.
    /// A consumer must re-read its data here; treating the reconnect as "nothing happened" is how a collection
    /// list silently omits a payment.
    /// </summary>
    public event Action? ConnectionRestored;

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null)
                return; // already started

            var hubUrl = MauiProgram.GetApiBaseUrl().TrimEnd('/') + "/hubs/online-payments";

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        await tokenStore.InitializeAsync();
                        return tokenStore.AccessToken;
                    };
                })
                .WithAutomaticReconnect(new HubRetryForeverPolicy())
                .Build();

            connection.On<OnlinePaymentNotification>(
                "OnlinePaymentReceived", n => PaymentReceived?.Invoke(n));

            connection.Reconnected += _ =>
            {
                // The gap is the event. Whatever arrived while the socket was down is gone, so the consumer is
                // told to go and look rather than left believing it saw everything.
                ConnectionRestored?.Invoke();
                return Task.CompletedTask;
            };

            connection.Closed += _ =>
            {
                // Once the connection is truly closed, the field must not keep pointing at it: StartAsync returns
                // early when it is not null, so a dead connection left in place meant every later attempt
                // short-circuited and realtime stayed dead until the app was restarted.
                //
                // Deliberately WITHOUT taking the gate. StopAsync holds the gate while stopping the connection,
                // and the connection awaits this handler as part of stopping - waiting for the gate here would
                // deadlock the app on sign-out. A reference assignment is atomic, and the only interleaving that
                // matters is with StartAsync, which assigns a freshly built connection immediately afterwards.
                Interlocked.CompareExchange(ref _connection, null, connection);
                return Task.CompletedTask;
            };

            try
            {
                // Bound the connect attempt: over a dead/flapping tunnel the negotiate can otherwise hang,
                // holding the gate (and any awaiting caller) indefinitely. On timeout we treat it as a
                // failed best-effort connect and move on.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await connection.StartAsync(cts.Token);
                _connection = connection;
            }
            catch
            {
                // Realtime is a non-critical enhancement; the collection list still works without it.
                await connection.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                try { await _connection.StopAsync(); } catch { /* ignore */ }
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
