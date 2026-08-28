using EEMOCantilanSDS.Application.Common.Interface.Time;
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
    IUnitOfWork unitOfWork, IClock clock) : IRequestHandler<InitiateOnlinePaymentCommand, Result<InitiateOnlinePaymentResultDto>>
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
                "This billing period isn't covered by an active contract for this stall.", ResultStatus.Conflict);

        // NPM is daily-billed (no monthly record). Online pays a whole month of the base ₱30 daily fee —
        // the same unpaid, elapsed, in-term, non-closed days the staff month-settle would cover (fish ₱/kg
        // is weighed at the stall and utilities are billed separately, so both are excluded here).
        if (stall.Facility?.Code == FacilityCode.NPM)
            return request.Kind switch
            {
                PayorPayableKind.NpmUtility => await InitiateNpmUtilityAsync(stall, payorId.Value, request, cancellationToken),
                // Several fish days, each with the kilos declared for it. A fish day's price is that day's own weighing
                // fee on top of the stall's daily fee, so the days are remembered one by one and settled that way.
                PayorPayableKind.NpmFish when request.FishDays is { Count: > 1 }
                    => await InitiateNpmFishDaysAsync(stall, payorId.Value, request, cancellationToken),
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
                $"EEMO online payment · {record.PeriodKey}",
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

    // NPM daily-month checkout: amount = base ₱30 × the month's unpaid, elapsed, in-term, non-closed days
    // (from the shared settlement service, so the charge equals what settlement will mark). No PaymentRecord.
    private async Task<Result<InitiateOnlinePaymentResultDto>> InitiateNpmAsync(
        Domain.Entities.Facilities.Stall stall, Guid payorId, InitiateOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var payable = request.Days is { } askedDays && askedDays > 0
            ? await npmMonthSettlementService.ComputePayableForDaysAsync(stall, request.Year, request.Month, askedDays, cancellationToken)
            : await npmMonthSettlementService.ComputePayableAsync(stall, request.Year, request.Month, cancellationToken);

        // Asked for a number of days and the month cannot answer for that many: some were collected at the stall since
        // the payor looked, or the month has closed and is settled as one figure. Refused rather than quietly charging
        // for a different number of days than the payor was shown, which is the same rule the stale-checkout guard below
        // applies to money.
        if (request.Days is { } asked && payable.Days != asked)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                "Those days have changed since you last looked. Reload your balances and try again.", ResultStatus.Conflict);

        // An amount with no days is legitimate: a closed short month whose every day was collected still owes its
        // month-end adjustment, and refusing it would leave the payor no way to settle the month online.
        if (payable.Amount <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This period has no outstanding daily balance.", ResultStatus.Conflict);

        // Resume an unfinished checkout for the same stall+month rather than opening a duplicate, but only while it still
        // asks for the same money. A market stall owes another day's fee every day, so a session opened earlier in the
        // month is priced for fewer days than the payor now owes: that is how the balance read ₱240 and the gateway
        // charged ₱180 for a session started two days before.
        var resumable = await onlinePaymentRepository.GetResumableNpmTransactionAsync(stall.Id, request.Year, request.Month, OnlinePaymentTargetKind.NpmDailyMonth, cancellationToken);
        if (resumable is { IsResumable: true })
        {
            if (resumable.Amount == payable.Amount)
                return Result<InitiateOnlinePaymentResultDto>.Success(
                    new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

            resumable.MarkExpired(StaleCheckoutNote(resumable.Amount, payable.Amount));
        }

        string reference;
        do
        {
            reference = GenerateReference(clock.PhilippineNow);
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var transaction = OnlinePaymentTransaction.CreateForNpmMonth(
            reference, payorId, stall.Id, request.Year, request.Month, payable.Amount, paymentGateway.Provider);

        var periodKey = $"{request.Year:0000}-{request.Month:00}";
        // A part-month payment says how many days it covers, so the line on the gateway and on the office's own record
        // reads as what it is rather than as a month settled short.
        var description = request.Days is { } paidDays
            ? $"EEMO online payment · NPM daily · {periodKey} · {paidDays} {(paidDays == 1 ? "day" : "days")}"
            : $"EEMO online payment · NPM daily · {periodKey}";
        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                payable.Amount,
                reference,
                description,
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", ResultStatus.UpstreamFailed);

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
            return Result<InitiateOnlinePaymentResultDto>.Failure("This period has no outstanding utility balance.", ResultStatus.Conflict);

        var resumable = await onlinePaymentRepository.GetResumableNpmTransactionAsync(stall.Id, request.Year, request.Month, OnlinePaymentTargetKind.NpmUtilityBill, cancellationToken);
        if (resumable is { IsResumable: true })
        {
            // A meter reading corrected, or part of the bill paid at the office, changes what is owed. Resume only while
            // the old session still asks for it.
            if (resumable.Amount == bill.BalanceDue)
                return Result<InitiateOnlinePaymentResultDto>.Success(
                    new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

            resumable.MarkExpired(StaleCheckoutNote(resumable.Amount, bill.BalanceDue));
        }

        string reference;
        do
        {
            reference = GenerateReference(clock.PhilippineNow);
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
                checkout.Error ?? "Unable to start the online payment.", ResultStatus.UpstreamFailed);

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
        // One declaration is the same request said the newer way, so a caller need not carry two shapes for one day.
        var single = request.FishDays is { Count: 1 } only ? only[0] : null;
        var requestedDay = request.Day ?? single?.Day;
        var requestedKilos = request.FishKilos ?? single?.Kilos ?? (single is not null ? 0m : null);

        if (requestedDay is not { } dayOfMonth
            || dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(request.Year, request.Month))
            return Result<InitiateOnlinePaymentResultDto>.Failure("Pick a valid day to pay for.", ResultStatus.Invalid);
        if (requestedKilos is not { } kilos || kilos < 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("Enter the kilos for that day.", ResultStatus.Invalid);

        var day = new DateOnly(request.Year, request.Month, dayOfMonth);
        var quote = await npmMonthSettlementService.QuoteFishDayAsync(stall, day, kilos, cancellationToken);
        if (!quote.IsPayable)
            return Result<InitiateOnlinePaymentResultDto>.Failure(quote.Error ?? "That day can't be paid online.", ResultStatus.Conflict);
        if (quote.Amount <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("This day has no outstanding balance.", ResultStatus.Conflict);

        // Resume an unfinished checkout for the SAME stall + exact day rather than opening a duplicate, but only while it
        // asks for the same money. A payor who declared five kilos, backed out, and now declares eight must be charged for
        // eight, not sent back to the earlier session's amount.
        var resumable = await onlinePaymentRepository.GetResumableNpmFishDayTransactionAsync(stall.Id, request.Year, request.Month, dayOfMonth, cancellationToken);
        if (resumable is { IsResumable: true })
        {
            if (resumable.Amount == quote.Amount)
                return Result<InitiateOnlinePaymentResultDto>.Success(
                    new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

            resumable.MarkExpired(StaleCheckoutNote(resumable.Amount, quote.Amount));
        }

        string reference;
        do
        {
            reference = GenerateReference(clock.PhilippineNow);
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
                checkout.Error ?? "Unable to start the online payment.", ResultStatus.UpstreamFailed);

        transaction.SetPending(checkout.Value.GatewayReference, checkout.Value.CheckoutUrl);
        await onlinePaymentRepository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InitiateOnlinePaymentResultDto>.Success(
            new InitiateOnlinePaymentResultDto(checkout.Value.CheckoutUrl, reference));
    }

    /// <summary>
    /// Several NPM fish days in one payment, each priced from the kilos declared for THAT day.
    ///
    /// <para>
    /// The office's collectors have long settled several owed days at once. Online could pay only one, so a payor three
    /// days behind opened three checkouts and the office received three payments to receipt apart. Each day is priced by
    /// the same service that prices a single day, so the figure charged is the office's own; every day must still be
    /// payable, because a day collected at the stall since the payor looked would otherwise be paid for twice.
    /// </para>
    /// </summary>
    private async Task<Result<InitiateOnlinePaymentResultDto>> InitiateNpmFishDaysAsync(
        Domain.Entities.Facilities.Stall stall, Guid payorId, InitiateOnlinePaymentCommand request, CancellationToken cancellationToken)
    {
        var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);

        // One entry per day, in date order, kilos left out meaning none were sold. Ordered here so the same selection
        // always stores the same text, which is what the resume guard below compares.
        var declarations = new List<NpmFishDayDeclarations.Declaration>();
        foreach (var asked in request.FishDays!.GroupBy(f => f.Day).Select(g => g.First()).OrderBy(f => f.Day))
        {
            if (asked.Day < 1 || asked.Day > daysInMonth)
                return Result<InitiateOnlinePaymentResultDto>.Failure("Pick valid days to pay for.", ResultStatus.Invalid);

            var kilos = asked.Kilos ?? 0m;
            if (kilos < 0m)
                return Result<InitiateOnlinePaymentResultDto>.Failure("Kilos can't be negative.", ResultStatus.Invalid);

            declarations.Add(new NpmFishDayDeclarations.Declaration(asked.Day, kilos));
        }

        if (declarations.Count == 0)
            return Result<InitiateOnlinePaymentResultDto>.Failure("Pick at least one day to pay for.", ResultStatus.Invalid);

        // Priced day by day by the office's own rule, and every day has to be payable: not future, inside the term, not
        // a market closure, not already collected or excused.
        var amount = 0m;
        foreach (var declaration in declarations)
        {
            var day = new DateOnly(request.Year, request.Month, declaration.Day);
            var quote = await npmMonthSettlementService.QuoteFishDayAsync(stall, day, declaration.Kilos, cancellationToken);
            if (!quote.IsPayable)
                return Result<InitiateOnlinePaymentResultDto>.Failure(
                    quote.Error ?? "One of those days can't be paid online.", ResultStatus.Conflict);

            amount += quote.Amount;
        }

        if (amount <= 0m)
            return Result<InitiateOnlinePaymentResultDto>.Failure("These days have no outstanding balance.", ResultStatus.Conflict);

        var stored = NpmFishDayDeclarations.Format(declarations);

        // Resume an unfinished checkout only while it asks for the SAME money for the SAME days with the SAME kilos. A
        // payor who declared eight kilos for one of the days, backed out and now declares twelve must be charged for
        // twelve, and one who has since added another day must be charged for that day too.
        var resumable = await onlinePaymentRepository.GetResumableNpmTransactionAsync(
            stall.Id, request.Year, request.Month, OnlinePaymentTargetKind.NpmFishDays, cancellationToken);
        if (resumable is { IsResumable: true })
        {
            if (resumable.Amount == amount && resumable.FishDayDeclarations == stored)
                return Result<InitiateOnlinePaymentResultDto>.Success(
                    new InitiateOnlinePaymentResultDto(resumable.CheckoutUrl!, resumable.Reference));

            resumable.MarkExpired(StaleCheckoutNote(resumable.Amount, amount));
        }

        string reference;
        do
        {
            reference = GenerateReference(clock.PhilippineNow);
        }
        while (await onlinePaymentRepository.ReferenceExistsAsync(reference, cancellationToken));

        var transaction = OnlinePaymentTransaction.CreateForNpmFishDays(
            reference, payorId, stall.Id, request.Year, request.Month, declarations, amount, paymentGateway.Provider);

        var periodKey = $"{request.Year:0000}-{request.Month:00}";
        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CreateCheckoutSessionRequest(
                amount,
                reference,
                $"EEMO online payment · NPM fish · {periodKey} · {declarations.Count} days",
                urlBuilder.BuildSuccessUrl(reference),
                urlBuilder.BuildCancelUrl(reference)),
            cancellationToken);

        if (!checkout.IsSuccess || checkout.Value is null)
            return Result<InitiateOnlinePaymentResultDto>.Failure(
                checkout.Error ?? "Unable to start the online payment.", ResultStatus.UpstreamFailed);

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
