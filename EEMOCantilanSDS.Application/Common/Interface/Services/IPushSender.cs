namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Sends push notifications to collectors' registered devices. Implementations are best-effort: a missing
/// credential, no registered device, or a delivery failure must never throw into the caller — they return
/// the number of devices actually reached (0 when disabled/unreachable).
/// </summary>
public interface IPushSender
{
    /// <summary>
    /// Pushes a notification to every device registered to <paramref name="collectorId"/>. Prunes tokens
    /// the push service reports as permanently invalid. Returns the count of devices successfully sent to.
    /// </summary>
    Task<int> SendToCollectorAsync(
        Guid collectorId,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default);
}
