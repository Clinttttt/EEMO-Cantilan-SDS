using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Single, idempotent settlement path for a verified-paid online payment. Both the webhook (primary)
/// and the confirmation/reconciliation fallback delegate here so the "money received" logic — amount
/// integrity, marking the transaction paid, clearing the linked record, persisting, and the best-effort
/// staff notification — lives in exactly one place and cannot drift between the two callers.
/// </summary>
public interface IOnlinePaymentSettlementService
{
    /// <summary>
    /// Records <paramref name="transaction"/> as paid from a verified gateway event. Idempotent: a call
    /// for an already-settled transaction is a no-op success (no duplicate save, no duplicate notification).
    /// Returns 409 on an amount mismatch and 500 if the linked record is missing.
    /// </summary>
    Task<Result<bool>> SettleAsync(
        OnlinePaymentTransaction transaction,
        PaymentGatewayEvent evt,
        CancellationToken cancellationToken = default);
}
