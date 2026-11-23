using EEMOCantilanSDS.Mobile.Abstractions;
using Microsoft.Maui.Networking;

namespace EEMOCantilanSDS.Mobile.Platform;

/// <summary>
/// MAUI-backed <see cref="IConnectivityMonitor"/> wrapping <c>Connectivity.Current</c>. This is the thin
/// platform glue that keeps <see cref="MobileSyncService"/> (in EEMOCantilanSDS.Mobile.Core) free of any
/// MAUI dependency so its logic stays unit-testable.
/// </summary>
public sealed class MauiConnectivityMonitor : IConnectivityMonitor
{
    // A Debug build pointed at an API on this machine reaches it whatever Windows says about the internet, so only a failed request
    // means offline there. Release builds use the production API (never loopback) and keep the platform's answer.
    private readonly bool _apiOnThisMachine = Uri.TryCreate(MauiProgram.GetApiBaseUrl(), UriKind.Absolute, out var api) && api.IsLoopback;

    public MauiConnectivityMonitor()
    {
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOnline => _apiOnThisMachine || Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event Action? ConnectivityRestored;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            ConnectivityRestored?.Invoke();
    }
}
