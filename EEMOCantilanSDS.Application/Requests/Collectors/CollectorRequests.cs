namespace EEMOCantilanSDS.Application.Requests.Collectors;

public record ToggleCollectorStatusRequest(bool IsActive);

public record ResetCollectorPasswordRequest(string NewPassword, string ConfirmPassword);

/// <summary>
/// Cash the office has received from a collector. The collector is named by the route, so the body carries only what the
/// receiving officer typed.
/// </summary>
/// <param name="ReceivedAt">
/// The office's own wall clock. Null means now, which is the ordinary case; a time is stated where the money was handed
/// over before it could be entered.
/// </param>
public record RecordCollectorRemittanceRequest(
    decimal Amount,
    DateOnly CoversFrom,
    DateOnly CoversTo,
    DateTime? ReceivedAt = null,
    string? ReferenceNo = null,
    string? Notes = null);
