using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;

public class InitiateOnlinePaymentCommandHandler(
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    IPayorRepository payorRepository,
    IPaymentGateway paymentGateway,
    IOnlinePaymentUrlBuilder urlBuilder,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<InitiateOnlinePaymentCommand, Result<InitiateOnlinePaymentResultDto>>
{
    public async Task<Result<InitiateOnlinePaymentResultDto>> Handle(InitiateOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var payorId = currentUser.UserId;
        if (payorId is null)
            return Result<InitiateOnlinePaymentResultDto>.Unauthorized();

        // The payor may only pay for stalls linked to their own account.
        if (!await payorRepository.LinkExistsAsync(payorId.Value, request.StallId, cancellationToken))
            return Result<InitiateOnlinePaymentResultDto>.Forbidden();

        var stall = await stallRepository.GetByIdAsync(request.StallId, cancellationToken);
        if (stall is null)
            return Result<InitiateOnlinePaymentResultDto>.NotFound();

        // v1 online payment is for monthly-rental facilities; NPM is daily-billed (out of scope here).
        if (stall.Facility?.Code == FacilityCode.NPM)
            return Result<InitiateOnlinePaymentResultDto>.Failure("Online payment is not available for this facility yet.", 409);

        // Find-or-create the monthly record (a current-month obligation may not have a row yet).
        var isNewRecord = false;
        var existingDto = await paymentRepository.GetPaymentRecordAsync(request.StallId, request.Year, request.Month, cancellationToken);
        PaymentRecord? record;
        if (existingDto is not null)
        {
            record = await paymentRepository.GetByIdAsync(existingDto.Id, cancellationToken);
            if (record is null)
                return Result<InitiateOnlinePaymentResultDto>.NotFound();
        }
        else
        {
            record = PaymentRecord.Create(request.StallId, request.Year, request.Month, stall.MonthlyRate, "Online");
            isNewRecord = true;
        }

        // Only an outstanding balance is payable (full balance — no partial online payments in v1).
        if (record.Status == PaymentStatus.Paid || record.BalanceDue <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This period has no outstanding balance.", 409);

        // If the payor already has an unfinished checkout for this period (e.g. they backed out), send
        // them back to the SAME session rather than opening a duplicate — this is the double-payment guard.
        if (!isNewRecord)
        {
            var resumable = await onlinePaymentRepository.GetResumableTransactionForRecordAsync(record.Id, cancellationToken);
            if (resumable is { IsResumable: true })
                return Result<InitiateOnlinePaymentResultDto>.Success(
                    new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));
        }

        var amount = record.BalanceDue;

        string reference;
        do
        {
            reference = GenerateReference();
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var transaction = OnlinePaymentTransaction.Create(
            reference, payorId.Value, record.Id, amount, paymentGateway.Provider);

        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                amount,
                reference,
                $"EEMO online payment · {record.PeriodKey}",
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", 502);

        // Persist only after the gateway accepted the session.
        if (isNewRecord)
            await paymentRepository.AddAsync(record, cancellationToken);

        transaction.SetPending(checkout.Value.GatewayReference, checkout.Value.CheckoutUrl);
        await onlinePaymentRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InitiateOnlinePaymentResultDto>.Success(
            new InitiateOnlinePaymentResultDto(checkout.Value.CheckoutUrl, reference));
    }

    private static string GenerateReference() =>
        $"EEMO-OP-{PhilippineTime.Now:yyyyMMdd}-{Guid.NewGuid():N}"[..25].ToUpperInvariant();
}
