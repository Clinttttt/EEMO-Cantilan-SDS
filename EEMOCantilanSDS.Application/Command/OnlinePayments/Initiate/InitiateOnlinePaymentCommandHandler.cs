using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
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
    INpmMonthSettlementService npmMonthSettlementService,
    IUtilityBillRepository utilityBillRepository,
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

        // The requested period must fall within one of the stall's contract terms. Without this a payor
        // linked to a stall could pay for months the stall isn't contracted for (before move-in, after
        // expiry, or arbitrary future months), creating obligation rows for uncovered periods.
        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));
        if (!stall.Contracts.Any(c => c.OverlapsPeriod(periodStart, periodEnd)))
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                "This billing period isn't covered by an active contract for this stall.", 409);

        // NPM is daily-billed (no monthly record). Online pays a whole month of the base ₱30 daily fee —
        // the same unpaid, elapsed, in-term, non-closed days the staff month-settle would cover (fish ₱/kg
        // is weighed at the stall and utilities are billed separately, so both are excluded here).
        if (stall.Facility?.Code == FacilityCode.NPM)
            return request.Kind switch
            {
                PayorPayableKind.NpmUtility => await InitiateNpmUtilityAsync(stall, payorId.Value, request, cancellationToken),
                PayorPayableKind.NpmFish => await InitiateNpmFishDayAsync(stall, payorId.Value, request, cancellationToken),
                _ => await InitiateNpmAsync(stall, payorId.Value, request, cancellationToken)
            };

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

    // NPM daily-month checkout: amount = base ₱30 × the month's unpaid, elapsed, in-term, non-closed days
    // (from the shared settlement service, so the charge equals what settlement will mark). No PaymentRecord.
    private async Task<Result<InitiateOnlinePaymentResultDto>> InitiateNpmAsync(
        Domain.Entities.Facilities.Stall stall, Guid payorId, InitiateOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var payable = await npmMonthSettlementService.ComputePayableAsync(stall, request.Year, request.Month, cancellationToken);
        if (payable.Days <= 0 || payable.Amount <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This period has no outstanding daily balance.", 409);

        // Resume an unfinished checkout for the same stall+month rather than opening a duplicate.
        var resumable = await onlinePaymentRepository.GetResumableNpmTransactionAsync(stall.Id, request.Year, request.Month, OnlinePaymentTargetKind.NpmDailyMonth, cancellationToken);
        if (resumable is { IsResumable: true })
            return Result<InitiateOnlinePaymentResultDto>.Success(
                new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

        string reference;
        do
        {
            reference = GenerateReference();
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var transaction = OnlinePaymentTransaction.CreateForNpmMonth(
            reference, payorId, stall.Id, request.Year, request.Month, payable.Amount, paymentGateway.Provider);

        var periodKey = $"{request.Year:0000}-{request.Month:00}";
        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                payable.Amount,
                reference,
                $"EEMO online payment · NPM daily · {periodKey}",
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", 502);

        transaction.SetPending(checkout.Value.GatewayReference, checkout.Value.CheckoutUrl);
        await onlinePaymentRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InitiateOnlinePaymentResultDto>.Success(
            new InitiateOnlinePaymentResultDto(checkout.Value.CheckoutUrl, reference));
    }

    // NPM electricity + water: charge the current outstanding balance of the month's UtilityBill (elec +
    // water together, full balance). No PaymentRecord; settlement marks the bill's unpaid utilities Paid.
    private async Task<Result<InitiateOnlinePaymentResultDto>> InitiateNpmUtilityAsync(
        Domain.Entities.Facilities.Stall stall, Guid payorId, InitiateOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var bill = await utilityBillRepository.GetByStallAndMonthAsync(stall.Id, request.Year, request.Month, cancellationToken);
        if (bill is null || bill.BalanceDue <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This period has no outstanding utility balance.", 409);

        var resumable = await onlinePaymentRepository.GetResumableNpmTransactionAsync(stall.Id, request.Year, request.Month, OnlinePaymentTargetKind.NpmUtilityBill, cancellationToken);
        if (resumable is { IsResumable: true })
            return Result<InitiateOnlinePaymentResultDto>.Success(
                new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

        string reference;
        do
        {
            reference = GenerateReference();
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var amount = bill.BalanceDue;
        var transaction = OnlinePaymentTransaction.CreateForNpmUtility(
            reference, payorId, stall.Id, request.Year, request.Month, amount, paymentGateway.Provider);

        var periodKey = $"{request.Year:0000}-{request.Month:00}";
        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                amount,
                reference,
                $"EEMO online payment · NPM utilities · {periodKey}",
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", 502);

        transaction.SetPending(checkout.Value.GatewayReference, checkout.Value.CheckoutUrl);
        await onlinePaymentRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InitiateOnlinePaymentResultDto>.Success(
            new InitiateOnlinePaymentResultDto(checkout.Value.CheckoutUrl, reference));
    }

    // NPM fish DAY: the payor self-declares kilos for ONE uncollected day; amount = base ₱30 + kilos ×
    // fish ₱/kg, both resolved as-of the day from the current municipality's snapshot (tenant-aware, so
    // custom LGUs use their own rates). Settlement marks just that day paid with the declared kilos.
    private async Task<Result<InitiateOnlinePaymentResultDto>> InitiateNpmFishDayAsync(
        Domain.Entities.Facilities.Stall stall, Guid payorId, InitiateOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        if (request.Day is not { } dayOfMonth
            || dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(request.Year, request.Month))
            return Result<InitiateOnlinePaymentResultDto>.Failure("Pick a valid day to pay for.", 400);
        if (request.FishKilos is not { } kilos || kilos < 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("Enter the kilos for that day.", 400);

        var day = new DateOnly(request.Year, request.Month, dayOfMonth);
        var quote = await npmMonthSettlementService.QuoteFishDayAsync(stall, day, kilos, cancellationToken);
        if (!quote.IsPayable)
            return Result<InitiateOnlinePaymentResultDto>.Failure(quote.Error ?? "That day can't be paid online.", 409);
        if (quote.Amount <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This day has no outstanding balance.", 409);

        // Resume an unfinished checkout for the SAME stall + exact day rather than opening a duplicate.
        var resumable = await onlinePaymentRepository.GetResumableNpmFishDayTransactionAsync(stall.Id, request.Year, request.Month, dayOfMonth, cancellationToken);
        if (resumable is { IsResumable: true })
            return Result<InitiateOnlinePaymentResultDto>.Success(
                new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

        string reference;
        do
        {
            reference = GenerateReference();
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var transaction = OnlinePaymentTransaction.CreateForNpmFishDay(
            reference, payorId, stall.Id, request.Year, request.Month, dayOfMonth, kilos, quote.Amount, paymentGateway.Provider);

        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                quote.Amount,
                reference,
                $"EEMO online payment · NPM fish · {day:yyyy-MM-dd}",
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", 502);

        transaction.SetPending(checkout.Value.GatewayReference, checkout.Value.CheckoutUrl);
        await onlinePaymentRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InitiateOnlinePaymentResultDto>.Success(
            new InitiateOnlinePaymentResultDto(checkout.Value.CheckoutUrl, reference));
    }

    private static string GenerateReference() =>
        $"EEMO-OP-{PhilippineTime.Now:yyyyMMdd}-{Guid.NewGuid():N}"[..25].ToUpperInvariant();
}
