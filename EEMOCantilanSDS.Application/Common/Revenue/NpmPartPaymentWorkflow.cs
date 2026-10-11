using System.Globalization;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Records the actual amount an NPM payor handed over against the installments the month owes TODAY — as full or part installments,
/// oldest first — through the existing canonical poster. One payment is one Collection (one SRC) with one allocation per installment
/// it reaches; a later payment against the same installment is another Collection, and neither rewrites the other. An amount above
/// what is owed is refused: it is never kept as an advance, never a future day's payment, never an unapplied credit.
/// </summary>
public sealed class NpmPartPaymentWorkflow(IStallRepository stalls, ICollectorRepository collectors,
    IDailyCollectionRepository days, INpmMonthSettlementService settlement, NpmDailyCanonicalPoster poster,
    ICurrentUserService user, IClock clock, IEemoCacheInvalidator cache, ITenantContext tenant, NpmPartPaymentOptions? options = null)
{
    public async Task<Result<NpmPartPaymentQuoteDto>> QuoteAsync(Guid stallId, int year, int month, decimal amount, CancellationToken ct = default, DateOnly? collectionDate = null)
    {
        // Off until the office has verified every report that reads NPM day rows against part payments (see the handoff).
        if (options?.Enabled != true)
            return Result<NpmPartPaymentQuoteDto>.Failure("Part payments are not enabled yet.", ResultStatus.Conflict);
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return Result<NpmPartPaymentQuoteDto>.Failure("Choose a valid settlement period.", ResultStatus.Invalid);
        if (!await NpmSettlementAccess.MaySettleMarketCollectionsAsync(user, collectors, ct))
            return Result<NpmPartPaymentQuoteDto>.Forbidden();
        var stall = await stalls.GetByIdAsync(stallId, ct);
        if (stall?.Facility?.Code != FacilityCode.NPM) return Result<NpmPartPaymentQuoteDto>.NotFound();
        var businessDate = collectionDate ?? clock.PhilippineToday;
        if (businessDate == default || businessDate > clock.PhilippineToday)
            return Result<NpmPartPaymentQuoteDto>.Failure("Choose a valid business date.", ResultStatus.Invalid);
        // Several Collections against one day need canonical authority: a legacy row can carry only one payment event.
        if (!await poster.IsCanonicalAsync(businessDate, ct))
            return Result<NpmPartPaymentQuoteDto>.Failure("Part payments are available after NPM collection activation.", ResultStatus.Conflict);
        var instrument = await poster.GetInstrumentAsync(businessDate, ct);
        if (instrument is null) return Result<NpmPartPaymentQuoteDto>.Failure("The approved collection policy is unavailable.", ResultStatus.Conflict);

        var plan = await settlement.PlanPartPaymentAsync(stall, year, month, amount, ct);
        return Result<NpmPartPaymentQuoteDto>.Success(new(stallId, year, month, businessDate, plan.Outcome, plan.Message, plan.Amount, plan.Applied,
            plan.Payable, plan.RemainingAfter, plan.Adjustment, plan.Unallocated,
            plan.Lines.Select(l => new NpmPartPaymentLineDto(l.Day, l.Fee, l.Settled, l.Applied, l.Remaining, l.Status)).ToList(),
            plan.Token, instrument));
    }

    public async Task<Result<NpmDailyCanonicalPoster.Outcome>> PostAsync(NpmPartPaymentRequest request, CancellationToken ct = default)
    {
        if (request.ClientOperationId == Guid.Empty || request.Amount <= 0m || string.IsNullOrWhiteSpace(request.QuoteToken))
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("A reviewed part-payment quote is required.", ResultStatus.Invalid);
        if (!await NpmSettlementAccess.MaySettleMarketCollectionsAsync(user, collectors, ct))
            return Result<NpmDailyCanonicalPoster.Outcome>.Forbidden();
        var intent = poster.NormalizeIntent("NpmPartPayment", request.StallId, [], request.BusinessDate,
            $"{request.Year:D4}-{request.Month:D2}|{request.Amount.ToString(CultureInfo.InvariantCulture)}|{request.QuoteToken}");
        if (await poster.FindPriorAsync(request.ClientOperationId, intent, ct) is { } prior) return prior;
        if (request.BusinessDate > clock.PhilippineToday || !await poster.IsCanonicalAsync(request.BusinessDate, ct))
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("This payment date is not under canonical NPM authority.", ResultStatus.Conflict);

        await using var transaction = poster.HasActiveTransaction ? null : await poster.BeginSettlementTransactionAsync(ct);
        var quoted = await QuoteAsync(request.StallId, request.Year, request.Month, request.Amount, ct, request.BusinessDate);
        if (!quoted.IsSuccess) return Result<NpmDailyCanonicalPoster.Outcome>.Failure(quoted.Error!, quoted.Status);
        var quote = quoted.Value!;
        // The month moved since the payment was reviewed (another payment, a correction, a closure): never apply it to a different month.
        if (quote.QuoteToken != request.QuoteToken)
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("RECONCILIATION_REQUIRED: The month balance changed. Review the received payment with the office.", ResultStatus.Conflict);
        if (quote.Outcome != "Applied")
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure(quote.Message ?? "The payment cannot be applied.", ResultStatus.Invalid);

        var stall = (await stalls.GetByIdAsync(request.StallId, ct))!;
        var recordedBy = user.Username ?? "Collector";
        var charges = new List<NpmDailyCanonicalPoster.Charge>();
        foreach (var line in quote.Lines.Where(l => l.Applied > 0m))
        {
            var day = await days.GetByStallAndDateAsync(stall.Id, line.Day, ct);
            if (day is null)
            {
                day = DailyCollection.Create(stall.Id, line.Day, recordedBy, line.Fee);
                await days.AddAsync(day, ct);
            }
            charges.Add(new NpmDailyCanonicalPoster.Charge(day, line.Applied, PartPayment: true));
        }
        if (charges.Sum(c => c.Amount) != request.Amount)
            throw new InvalidOperationException("NPM quote and settlement disagreed; nothing may be posted.");
        Result<NpmDailyCanonicalPoster.Outcome> result;
        try
        {
            result = await poster.PostAsync(stall, charges, request.ClientOperationId, intent, request.BusinessDate, ct);
            if (result.IsSuccess && transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (System.Data.Common.DbException)
        {
            // A concurrent payment against the same installment won the serializable race: nothing of this attempt is saved, and the
            // collector is told to review the refreshed month. Retrying the same operation resolves to the winner when it was its own.
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure(
                "RECONCILIATION_REQUIRED: Another payment for this stall was recorded at the same time. Review the refreshed balance.", ResultStatus.Conflict);
        }
        if (result.IsSuccess)
            await cache.InvalidatePaymentAffectedViewsAsync(tenant.TenantCode, FacilityCode.NPM, request.Year, request.Month, ct);
        return result;
    }
}
