using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

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

        // Already settled (this confirm raced the webhook, or it's a repeat return) — idempotent success.
        if (transaction.IsSettled)
            return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Paid", true));

        // Terminal non-paid states (Failed/Cancelled/Expired) are not re-verified.
        if (transaction.IsTerminal)
            return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto(transaction.Status.ToString(), false));

        // No gateway hand-off recorded yet — nothing to verify; report pending.
        if (string.IsNullOrWhiteSpace(transaction.GatewayReference))
            return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Pending", false));

        // Verify the real status directly with the provider (server secret key).
        var statusResult = await paymentGateway.RetrievePaymentStatusAsync(transaction.GatewayReference, cancellationToken);
        if (!statusResult.IsSuccess || statusResult.Value is null)
            return Result<ConfirmOnlinePaymentResultDto>.Failure(
                statusResult.Error ?? "Could not verify the payment with the provider.", 502);

        var evt = statusResult.Value;
        switch (evt.Type)
        {
            case PaymentGatewayEventType.Paid:
                // Single, idempotent settle path shared with the webhook handler.
                var settle = await settlementService.SettleAsync(transaction, evt, cancellationToken);
                if (!settle.IsSuccess)
                    return Result<ConfirmOnlinePaymentResultDto>.Failure(
                        settle.Error ?? "Could not settle the payment.", settle.StatusCode ?? 500);

                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Paid", true));

            case PaymentGatewayEventType.Expired:
                transaction.MarkExpired(evt.RawPayload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Expired", false));

            case PaymentGatewayEventType.Failed:
                transaction.MarkFailed(evt.RawPayload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Failed", false));

            default:
                // Still pending/unpaid at the provider — leave as-is; the payor can retry shortly.
                return Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Pending", false));
        }
    }
}
