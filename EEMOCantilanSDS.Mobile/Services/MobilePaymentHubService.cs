using EEMOCantilanSDS.Application.Common.Payments;
using Microsoft.AspNetCore.SignalR.Client;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Connects the collector app to the API's online-payment hub and raises <see cref="PaymentReceived"/>
/// when a payor pays online. Best-effort: connection failures never throw to callers. The access token
/// is pulled fresh from <see cref="MobileTokenStore"/> on every (re)connect so it survives token refresh.
/// </summary>
public sealed class MobilePaymentHubService(MobileTokenStore tokenStore) : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised (off the UI thread) when an online payment is received.</summary>
    public event Action<OnlinePaymentNotification>? PaymentReceived;

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
                .WithAutomaticReconnect()
                .Build();

            connection.On<OnlinePaymentNotification>(
                "OnlinePaymentReceived", n => PaymentReceived?.Invoke(n));

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
