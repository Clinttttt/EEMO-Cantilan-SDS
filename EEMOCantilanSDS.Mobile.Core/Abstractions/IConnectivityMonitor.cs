namespace EEMOCantilanSDS.Mobile.Abstractions;

/// <summary>
/// Platform-agnostic connectivity signal consumed by <see cref="MobileSyncService"/>. The MAUI app
/// supplies an implementation backed by <c>Connectivity.Current</c>; tests supply a fake. Keeping the
/// sync logic off the MAUI static surface is what makes it unit-testable.
/// </summary>
public interface IConnectivityMonitor
{
    /// <summary>True when the device currently has full internet access.</summary>
    bool IsOnline { get; }

    /// <summary>Raised when connectivity is restored (transition into internet access).</summary>
    event Action? ConnectivityRestored;
}
