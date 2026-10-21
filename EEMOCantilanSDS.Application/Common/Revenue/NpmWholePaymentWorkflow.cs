using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Mobile contract around the existing month rules and canonical NPM poster; no device calculation.</summary>
public sealed class NpmWholePaymentWorkflow(IStallRepository stalls, ICollectorRepository collectors,
    IDailyCollectionRepository days, INpmMonthSettlementService settlement, NpmDailyCanonicalPoster poster,
    ICurrentUserService user, IClock clock, IEemoCacheInvalidator cache, ITenantContext tenant,
    NpmRentMonthWorkflow? monthly = null)
{
    public async Task<Result<NpmWholePaymentQuoteDto>> QuoteAsync(Guid stallId, int year, int month, CancellationToken ct = default, DateOnly? collectionDate = null)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return Result<NpmWholePaymentQuoteDto>.Failure("Choose a valid settlement period.", ResultStatus.Invalid);
        if (!await NpmSettlementAccess.MaySettleMarketCollectionsAsync(user, collectors, ct))
            return Result<NpmWholePaymentQuoteDto>.Forbidden();
        var stall = await stalls.GetByIdAsync(stallId, ct);
        if (stall?.Facility?.Code != FacilityCode.NPM || stall.Facility.IsMonthlyRentMarket) return Result<NpmWholePaymentQuoteDto>.NotFound();
        var businessDate = collectionDate ?? clock.PhilippineToday;
        if (businessDate == default || businessDate > clock.PhilippineToday) return Result<NpmWholePaymentQuoteDto>.Failure("Choose a valid business date.");
        if (!await poster.IsCanonicalAsync(businessDate, ct))
            return Result<NpmWholePaymentQuoteDto>.Failure("Whole payment is available after NPM collection activation.", ResultStatus.Conflict);
        var instrument = await poster.GetInstrumentAsync(businessDate, ct);
        if (instrument is null) return Result<NpmWholePaymentQuoteDto>.Failure("The approved collection policy is unavailable.", ResultStatus.Conflict);
        if (monthly is not null && await monthly.MustUseMonthlyAuthorityAsync(stallId, new(year, month, 1), ct))
        {
            var result = await monthly.QuoteAsync(stallId, new(year, month, 1), businessDate, NpmRentPaymentScope.WholeMonth, ct);
            if (!result.IsSuccess) return Result<NpmWholePaymentQuoteDto>.Failure(result.Error!, result.Status);
            var q = result.Value!;
            return Result<NpmWholePaymentQuoteDto>.Success(new(stallId, year, month, businessDate, 0,
                q.EligibleAmount, 0, q.Token, q.Instrument, q.Assessment, q.Received, q.Credits,
                CollectionSourceKind.NpmRentMonth, q.Version, q.ContractId));
        }
        // The month owes its approved monthly obligation in full, not the days the calendar has reached: some payors pay ahead.
        var payable = await settlement.ComputeWholeMonthPayableAsync(stall, year, month, ct);
        var eligible = await settlement.GetWholeMonthPayableDaysAsync(stall, year, month, ct);
        var facts = string.Join("|", eligible.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            + $"|{payable.Days}|{payable.Amount.ToString(CultureInfo.InvariantCulture)}|{payable.Adjustment.ToString(CultureInfo.InvariantCulture)}";
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts)));
        return Result<NpmWholePaymentQuoteDto>.Success(new(stallId, year, month, businessDate,
            payable.Days, payable.Amount, payable.Adjustment, token, instrument.Value, payable.Obligation, payable.Collected, payable.Credits));
    }

    public async Task<Result<NpmDailyCanonicalPoster.Outcome>> PostAsync(NpmWholePaymentRequest request, CancellationToken ct = default)
    {
        if (request.ClientOperationId == Guid.Empty || request.Amount <= 0m || string.IsNullOrWhiteSpace(request.QuoteToken))
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("A reviewed whole-payment quote is required.", ResultStatus.Invalid);
        if (!await NpmSettlementAccess.MaySettleMarketCollectionsAsync(user, collectors, ct))
            return Result<NpmDailyCanonicalPoster.Outcome>.Forbidden();
        if (request.Year is < 2000 or > 2100 || request.Month is < 1 or > 12)
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("Choose a valid settlement period.", ResultStatus.Invalid);
        if (monthly is not null && await monthly.MustUseMonthlyAuthorityAsync(request.StallId, new(request.Year, request.Month, 1), ct))
        {
            if (!await poster.IsCanonicalAsync(request.BusinessDate, ct))
                return Result<NpmDailyCanonicalPoster.Outcome>.Failure("This date is not under canonical NPM authority.", ResultStatus.Conflict);
            var outcome = await monthly.PostAsync(new(request.ClientOperationId, request.StallId,
                new(request.Year, request.Month, 1), request.BusinessDate, NpmRentPaymentScope.WholeMonth, request.Amount, request.QuoteToken,
                OfficialReceiptNumber: request.OfficialReceiptNumber), ct);
            if (!outcome.IsSuccess) return Result<NpmDailyCanonicalPoster.Outcome>.Failure(outcome.Error!, outcome.Status);
            await cache.InvalidatePaymentAffectedViewsAsync(tenant.TenantCode, FacilityCode.NPM, request.Year, request.Month, ct);
            var saved = outcome.Value!;
            return Result<NpmDailyCanonicalPoster.Outcome>.Success(new(saved.CollectionId, saved.ReferenceCode, saved.Amount, saved.Existing));
        }
        var intent = poster.NormalizeIntent("NpmWholePayment", request.StallId, [], request.BusinessDate,
            $"{request.Year:D4}-{request.Month:D2}|{request.Amount.ToString(CultureInfo.InvariantCulture)}|{request.QuoteToken}" + NpmReceiptEvidence.IntentSuffix(request.OfficialReceiptNumber));
        if (await poster.FindPriorAsync(request.ClientOperationId, intent, ct) is { } prior) return prior;
        if (request.BusinessDate > clock.PhilippineToday || !await poster.IsCanonicalAsync(request.BusinessDate, ct))
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("This payment date is not under canonical NPM authority.", ResultStatus.Conflict);
        await using var transaction = poster.HasActiveTransaction ? null : await poster.BeginSettlementTransactionAsync(ct);
        var quoted = await QuoteAsync(request.StallId, request.Year, request.Month, ct, request.BusinessDate);
        if (!quoted.IsSuccess) return Result<NpmDailyCanonicalPoster.Outcome>.Failure(quoted.Error!, quoted.Status);
        if (quoted.Value!.Amount != request.Amount || quoted.Value.QuoteToken != request.QuoteToken)
            return Result<NpmDailyCanonicalPoster.Outcome>.Failure("RECONCILIATION_REQUIRED: The month balance changed. Review the received payment with the office.", ResultStatus.Conflict);

        var stall = (await stalls.GetByIdAsync(request.StallId, ct))!;
        // An adjustment can ride on an earlier paid installment. Charge its delta only, never that installment again.
        var before = (await days.GetByStallAndMonthAsync(stall.Id, request.Year, request.Month, ct))
            .Where(d => d.IsPaid).ToDictionary(d => d.Id, d => (d.DailyFee, d.UpdatedAt, d.UpdatedBy));
        var settled = await settlement.SettleWholeMonthAsync(stall, request.Year, request.Month,
            user.CollectorId, user.Username ?? "Collector", ct, request.Amount);
        var charges = settled.Select(d => before.TryGetValue(d.Id, out var original)
            ? new NpmDailyCanonicalPoster.Charge(d, d.DailyFee - original.DailyFee, true, original.UpdatedAt, original.UpdatedBy)
            : new NpmDailyCanonicalPoster.Charge(d, d.DailyFee)).Where(c => c.Amount > 0m).ToList();
        if (charges.Sum(c => c.Amount) != request.Amount)
            throw new InvalidOperationException("NPM quote and settlement disagreed; nothing may be posted.");
        var result = await poster.PostAsync(stall, charges, request.ClientOperationId, intent, request.BusinessDate, ct, request.OfficialReceiptNumber);
        if (result.IsSuccess)
        {
            if (transaction is not null) await transaction.CommitAsync(ct);
            await cache.InvalidatePaymentAffectedViewsAsync(tenant.TenantCode, FacilityCode.NPM, request.Year, request.Month, ct);
        }
        return result;
    }
}
