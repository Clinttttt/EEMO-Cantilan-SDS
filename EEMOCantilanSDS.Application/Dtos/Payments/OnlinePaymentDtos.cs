using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>Returned to the payor after initiating: where to redirect, plus the internal reference.</summary>
public sealed record InitiateOnlinePaymentResultDto(string CheckoutUrl, string Reference);

/// <summary>
/// A reconciliation row for staff: an online payment whose money has been received but whose
/// Official Receipt has not yet been encoded.
/// </summary>
public sealed record OnlinePaymentAwaitingOrDto(
    Guid TransactionId,
    string Reference,
    FacilityCode Facility,
    string StallNo,
    string PayorName,
    string Period,
    decimal Amount,
    string? Method,
    DateTime? PaidAt);

/// <summary>Ordered by how much it needs someone: sorting by this puts the money waiting on a decision first.</summary>
public enum OnlinePaymentExceptionKind
{
    NeedsDecision = 1,
    Unconfirmed = 2,
    Failed = 3,
    Resolved = 4
}

public static class OnlinePaymentExceptionWindows
{
    /// <summary>A hosted checkout still open after this long is worth asking the gateway about.</summary>
    // Past OnlinePaymentReconciler.CloseAfter, so this only lists a checkout the reconciler could not settle or close, not one a payor just left open.
    public static readonly TimeSpan UnconfirmedAfter = TimeSpan.FromHours(25);
    public static readonly TimeSpan RecentHistory = TimeSpan.FromDays(30);
    public const int MaxRows = 200;
}

/// <summary>One row of the office's online-payment exceptions. <c>Amount</c> is what the checkout asked for; <c>CapturedAmount</c> is set only when the gateway took something else.</summary>
public sealed record OnlinePaymentExceptionDto(
    Guid TransactionId,
    string Reference,
    OnlinePaymentExceptionKind Kind,
    string Facility,
    string StallNo,
    string PayorName,
    string Period,
    decimal Amount,
    decimal? CapturedAmount,
    string? Method,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string? Reason,
    OnlinePaymentDisposition? Disposition,
    string? DispositionReference,
    string? DispositionNote,
    string? DispositionBy,
    DateTime? DispositionAtUtc);

/// <summary>
/// Treasury overview + recent history for the admin Online Payments page. All figures are drawn from the
/// LGU's OWN settled online-payment records (not the payment gateway), so they reconcile to the treasury
/// report. "Settled" = money received (Paid or OR-completed).
/// </summary>
public sealed record OnlinePaymentDashboardDto(
    decimal CollectedThisMonth,
    decimal CollectedThisYear,
    int SettledCountThisYear,
    string? TopMethod,
    int Year,
    IReadOnlyList<OnlinePaymentHistoryItemDto> Recent);

/// <summary>One received online payment for the history table (payor + facility resolved).</summary>
public sealed record OnlinePaymentHistoryItemDto(
    string Reference,
    string PayorName,
    string Facility,
    string StallNo,
    string Period,
    decimal Amount,
    string? Method,
    string Status,
    string? ORNumber,
    DateTime? PaidAt);
