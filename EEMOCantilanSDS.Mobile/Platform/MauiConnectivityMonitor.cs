using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Maui.Networking;

namespace EEMOCantilanSDS.Mobile.Platform;

/// <summary>
/// MAUI-backed <see cref="IConnectivityMonitor"/> wrapping <c>Connectivity.Current</c>. This is the thin
/// platform glue that keeps <see cref="MobileSyncService"/> (in EEMOCantilanSDS.Mobile.Core) free of any
/// MAUI dependency so its logic stays unit-testable.
/// </summary>
public sealed class MauiConnectivityMonitor : IConnectivityMonitor
{
    public MauiConnectivityMonitor()
    {
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event Action? ConnectivityRestored;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            ConnectivityRestored?.Invoke();
    }
}
