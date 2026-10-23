using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IOnlinePaymentRepository
{
    Task<OnlinePaymentTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Looks up a transaction by its gateway (checkout session) reference — the webhook dedupe key.</summary>
    Task<OnlinePaymentTransaction?> GetByGatewayReferenceAsync(string gatewayReference, CancellationToken ct = default);

    /// <summary>Looks up a transaction by its internal <c>Reference</c> (the value carried on the payor
    /// success/return URL) — used by the reconciliation/confirm fallback.</summary>
    Task<OnlinePaymentTransaction?> GetByReferenceAsync(string reference, CancellationToken ct = default);

    Task<bool> ReferenceExistsAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Returns the record's still-resumable transaction (Initiated/Pending) if one exists, so a payor
    /// who abandoned a checkout is sent back to the same session instead of opening a duplicate.
    /// Settled (Paid/Completed) periods are blocked earlier by the zero-balance check.
    /// </summary>
    Task<OnlinePaymentTransaction?> GetResumableTransactionForRecordAsync(Guid paymentRecordId, CancellationToken ct = default);

    /// <summary>
    /// NPM daily-month variant of the resumable lookup (there is no PaymentRecord): an unfinished
    /// checkout (Initiated/Pending) for the same stall + billing month AND target kind (daily fees vs
    /// the utility bill), so a payor who abandoned a checkout is sent back to the same session.
    /// </summary>
    Task<OnlinePaymentTransaction?> GetResumableNpmTransactionAsync(Guid stallId, int year, int month, OnlinePaymentTargetKind kind, CancellationToken ct = default);

    /// <summary>
    /// NPM fish-DAY variant of the resumable lookup: an unfinished checkout (Initiated/Pending) for the
    /// same stall + exact day, so a payor who abandoned a fish-day checkout is sent back to that session
    /// rather than opening a duplicate for the same day.
    /// </summary>
    Task<OnlinePaymentTransaction?> GetResumableNpmFishDayTransactionAsync(Guid stallId, int year, int month, int day, CancellationToken ct = default);

    /// <summary>Check for a captured payment that treasury must reconcile before merchant rotation,
    /// restricted to the requesting municipality.</summary>
    Task<bool> HasCapturedReconciliationAsync(Guid municipalityId, CancellationToken ct = default);

    /// <summary>Get tracked outstanding gateway-backed transactions for Head-confirmed TEST expiry.
    /// Returns up to <paramref name="limit"/> plus one for bounded-work safety.</summary>
    Task<IReadOnlyList<OnlinePaymentTransaction>> GetUnsettledMerchantCheckoutsAsync(
        Guid municipalityId, int limit, CancellationToken ct = default);

    /// <summary>Atomically expire an exact unpaid checkout only while its persisted state is
    /// Initiated or Pending. Returns false when a webhook or another request changed the row.</summary>
    Task<bool> TryExpireUnsettledCheckoutAsync(
        Guid municipalityId, Guid transactionId, string gatewayReference,
        string expiryEvidenceJson, CancellationToken ct = default);

    Task AddAsync(OnlinePaymentTransaction transaction, CancellationToken ct = default);

    /// <summary>Online payments that are Paid (money received) but still awaiting staff OR encoding.</summary>
    Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrAsync(CancellationToken ct = default);

    /// <summary>
    /// Period-scoped variant for the Follow-up History (past-period snapshot): online payments whose
    /// billing period is <paramref name="year"/>/<paramref name="month"/> and are Paid but still awaiting
    /// an OR. Lets a past month show only the online receipts that belonged to that period.
    /// </summary>
    Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrByPeriodAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// Treasury overview + recent settled history for the admin Online Payments page — computed from the
    /// LGU's own online-payment records (this-month / this-year collected, settled count, top method) plus
    /// the most recent <paramref name="recentLimit"/> received payments with payor + facility resolved.
    /// </summary>
    Task<OnlinePaymentDashboardDto> GetDashboardAsync(int year, int month, int recentLimit, CancellationToken ct = default);
}
