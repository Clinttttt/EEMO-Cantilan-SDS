namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// Result of a reconciliation/confirm attempt. <see cref="Settled"/> is true once the payment is
/// recorded as received (now or previously); <see cref="Status"/> is a human-friendly state
/// ("Paid", "Pending", "Expired", "Failed") for the payor's return screen.
/// </summary>
public record ConfirmOnlinePaymentResultDto(string Status, bool Settled);
