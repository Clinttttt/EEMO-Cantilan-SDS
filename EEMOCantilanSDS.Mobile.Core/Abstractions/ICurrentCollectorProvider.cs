namespace EEMOCantilanSDS.Mobile.Abstractions;

/// <summary>
/// Supplies a stable key for the currently-authenticated collector, used to tag and filter queued offline
/// operations so one collector never syncs another collector's captures on a shared/reassigned device.
/// The MAUI app backs this with the active session; tests supply a fake. Returns <c>null</c> when no
/// collector is signed in.
/// </summary>
public interface ICurrentCollectorProvider
{
    string? CollectorKey { get; }
}
