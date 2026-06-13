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
