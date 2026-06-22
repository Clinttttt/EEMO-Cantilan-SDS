using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Confirm;

/// <summary>
/// Reconciliation/confirm fallback for the online-payment flow. Verifies the pending transaction's
/// payment status directly with the gateway (server secret key) and settles it idempotently when paid.
/// Triggered on the payor's return from hosted checkout, or by staff reconciling a stuck transaction.
/// The webhook remains the primary settlement path; this is the safety net when a webhook never arrives.
/// </summary>
public record ConfirmOnlinePaymentCommand(string Reference) : IRequest<Result<ConfirmOnlinePaymentResultDto>>;
