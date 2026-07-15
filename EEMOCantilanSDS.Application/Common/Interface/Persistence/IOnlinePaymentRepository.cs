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

    Task AddAsync(OnlinePaymentTransaction transaction, CancellationToken ct = default);

    /// <summary>Online payments that are Paid (money received) but still awaiting staff OR encoding.</summary>
    Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrAsync(CancellationToken ct = default);

    /// <summary>
    /// Period-scoped variant for the Follow-up History (past-period snapshot): online payments whose
    /// billing period is <paramref name="year"/>/<paramref name="month"/> and are Paid but still awaiting
    /// an OR. Lets a past month show only the online receipts that belonged to that period.
    /// </summary>
    Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrByPeriodAsync(int year, int month, CancellationToken ct = default);
}
