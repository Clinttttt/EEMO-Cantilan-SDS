using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Confirm;

public class ConfirmOnlinePaymentCommandHandler(
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentGateway paymentGateway,
    IOnlinePaymentSettlementService settlementService,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<ConfirmOnlinePaymentCommand, Result<ConfirmOnlinePaymentResultDto>>
{
    public async Task<Result<ConfirmOnlinePaymentResultDto>> Handle(ConfirmOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var transaction = await onlinePaymentRepository.GetByReferenceAsync(request.Reference, cancellationToken);
        if (transaction is null)
            return Result<ConfirmOnlinePaymentResultDto>.NotFound();

        // Authorization: the owning payor may confirm their own payment; staff may reconcile any.
        var role = currentUser.Role;
        var isStaff = string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
        if (!isStaff && currentUser.UserId != transaction.PayorUserId)
            return Result<ConfirmOnlinePaymentResultDto>.Forbidden();

        // IsSettled also covers this state, but it only means "do not settle again". The money is not on the payor's balance until the Head decides.
        if (transaction.IsAwaitingOfficeDecision)
            return UnderReview();
        if (transaction.Disposition == OnlinePaymentDisposition.Refunded)
            return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Refunded", false));

        // Already settled (this confirm raced the webhook, or it's a repeat return) — idempotent success.
        if (transaction.IsSettled)
            return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Paid", true));

        // No gateway hand-off recorded yet — nothing to verify; report pending.
        if (string.IsNullOrWhiteSpace(transaction.GatewayReference))
            return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto(
                transaction.IsTerminal ? transaction.Status.ToString() : "Pending", false));

        // Verify the real status directly with the provider (server secret key).
        var statusResult = await paymentGateway.RetrievePaymentStatusAsync(transaction.GatewayReference, cancellationToken);
        if (!statusResult.IsSuccess || statusResult.Value is null)
            return Result<ConfirmOnlinePaymentResultDto>.Failure(
                statusResult.Error ?? "Could not verify the payment with the provider.", ResultStatus.UpstreamFailed);

        var evt = statusResult.Value;
        switch (evt.Type)
        {
            case PaymentGatewayEventType.Paid:
                // Single, idempotent settle path shared with the webhook handler. If a concurrent webhook
                // is settling the SAME transaction, xmin optimistic concurrency rejects the loser's write
                // and nothing double-settles.
                try
                {
                    var settle = await settlementService.SettleAsync(transaction, evt, cancellationToken);
                    if (transaction.Status == OnlinePaymentStatus.ReconciliationRequired)
                        return UnderReview();
                    if (!settle.IsSuccess)
                        return Result<ConfirmOnlinePaymentResultDto>.Failure(
                            settle.Error ?? "Could not settle the payment.", settle.StatusCode ?? 500);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // The winner may have stored a reconciliation rather than a settlement, so this is unread, not Paid. The next confirm reads it.
                    return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Pending", false));
                }

                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Paid", true));

            case PaymentGatewayEventType.Expired:
                transaction.MarkExpired(evt.RawPayload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Expired", false));

            case PaymentGatewayEventType.Failed:
                // The attempt failed but the checkout is still open and the payor can retry in it, so the row stays resumable.
                // Marking it Failed here would let Initiate open a second session while this one can still take money.
                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Failed", false));

            default:
                // Nothing paid at the provider. A Failed or Expired row stays what it is; only a live checkout is still pending.
                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto(
                    transaction.IsTerminal ? transaction.Status.ToString() : "Pending", false));
        }
    }

    private static Result<ConfirmOnlinePaymentResultDto> UnderReview() =>
        Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("UnderReview", false));
}
