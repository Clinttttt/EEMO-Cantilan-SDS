using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
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
    IUnitOfWork unitOfWork, IClock clock) : IRequestHandler<InitiateOnlinePaymentCommand, Result<InitiateOnlinePaymentResultDto>>
{
    /// <summary>Stable reason code (the validation-error key) for a checkout outside the approved online scope.</summary>
    public const string OutOfScopeCode = "ONLINE_PAYMENT_OUT_OF_SCOPE";

    /// <summary>
    /// The single rule for what can be paid online: a Monthly-kind charge on a monthly-rent facility. A daily-billed Public
    /// Market (NPM without the monthly-rent flag) is never online, whatever kind is sent. The portal's PayableOnline flag
    /// calls this too, so the button and the server guard cannot disagree.
    /// </summary>
    public static bool IsOnlineStallRent(Domain.Entities.Facilities.Facility? facility, PayorPayableKind kind) =>
        kind == PayorPayableKind.Monthly
        && facility is not null
        && facility.CustomCategory != CustomFacilityCategory.SpaceRental   // Space Rental is not Stall Rent
        && (facility.Code != FacilityCode.NPM || facility.IsMonthlyRentMarket);

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

        // Approved scope: NEW online checkout is monthly Stall Rent only - one stall + period, that period's full balance.
        // Everything else (daily-billed NPM in any form including its whole-month aggregate, fish days, utilities, any kind
        // but Monthly) is refused here: the real guard, the portal only hides the button. Settlement of transactions created
        // before this restriction does not pass through this handler and is unchanged.
        if (!IsOnlineStallRent(stall.Facility, request.Kind) || request.Days is > 0 || request.Day is not null
            || request.FishKilos is not null || request.FishDays is { Count: > 0 })
            return Result<InitiateOnlinePaymentResultDto>.ValidationFailure(new Dictionary<string, string[]>
            {
                [OutOfScopeCode] = ["Only the month's stall rent can be paid online, as the full balance. Pay this charge at the office or to your collector."]
            });

        // The requested period must fall within one of the stall's contract terms. Without this a payor
        // linked to a stall could pay for months the stall isn't contracted for (before move-in, after
        // expiry, or arbitrary future months), creating obligation rows for uncovered periods.
        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));
        if (!stall.Contracts.Any(c => c.OverlapsPeriod(periodStart, periodEnd)))
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                "This billing period isn't covered by an active contract for this stall.", ResultStatus.Conflict);

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
            record = PaymentRecord.Create(request.StallId, request.Year, request.Month,
                stall.MonthlyRentFor(request.Year, request.Month, clock.PhilippineToday), "Online");
            isNewRecord = true;
        }

        if (record.SettlementAuthorityState != SettlementAuthority.Legacy)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                "Online checkout is paused while this monthly source is transitioning to canonical settlement. Contact the office for reconciliation.",
                ResultStatus.Conflict);

        // Only an outstanding balance is payable (full balance — no partial online payments in v1).
        if (record.Status == PaymentStatus.Paid || record.BalanceDue <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This period has no outstanding balance.", ResultStatus.Conflict);

        var amount = record.BalanceDue;

        // If the payor already has an unfinished checkout for this period (e.g. they backed out), send them back to the
        // SAME session rather than opening a duplicate — that is the double-payment guard. Only while it still asks for
        // the same money, though: a session opened days ago was priced then, and a market stall owes another day's fee
        // every day. Resuming it showed the payor one figure on the balance and charged another at the gateway.
        if (!isNewRecord)
        {
            var resumable = await onlinePaymentRepository.GetResumableTransactionForRecordAsync(record.Id, cancellationToken);
            if (resumable is { IsResumable: true })
            {
                if (resumable.Amount == amount)
                    return Result<InitiateOnlinePaymentResultDto>.Success(
                        new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

                // Retired rather than left Pending, so the old link cannot be paid for the wrong amount afterwards.
                resumable.MarkExpired(StaleCheckoutNote(resumable.Amount, amount));
            }
        }

        string reference;
        do
        {
            reference = GenerateReference(clock.PhilippineNow);
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var transaction = OnlinePaymentTransaction.Create(
            reference, payorId.Value, record.Id, amount, paymentGateway.Provider);

        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                amount,
                reference,
                $"MEEDO online payment · {record.PeriodKey}",
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", ResultStatus.UpstreamFailed);

        // Persist only after the gateway accepted the session.
        if (isNewRecord)
            await paymentRepository.AddAsync(record, cancellationToken);

        transaction.SetPending(checkout.Value.GatewayReference, checkout.Value.CheckoutUrl);
        await onlinePaymentRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InitiateOnlinePaymentResultDto>.Success(
            new InitiateOnlinePaymentResultDto(checkout.Value.CheckoutUrl, reference));
    }

    /// <param name="now">Passed in rather than read: the reference carries the date it was issued, and a static helper that
    /// reaches for a clock cannot be tested.</param>
    /// <summary>
    /// What is written against a checkout retired because the money moved on. Kept as the transaction's payload so the
    /// office can see why a session it may remember was superseded, rather than finding a bare Expired row.
    /// </summary>
    private static string StaleCheckoutNote(decimal was, decimal now) =>
        $"{{\"supersededBy\":\"re-initiated\",\"reason\":\"amount changed\",\"was\":{was},\"now\":{now}}}";

    private static string GenerateReference(DateTime now) =>
        $"EEMO-OP-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..25].ToUpperInvariant();
}
