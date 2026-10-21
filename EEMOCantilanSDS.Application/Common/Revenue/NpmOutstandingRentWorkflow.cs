using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>One real receipt across the selected occupancy's existing collectible periods; never creates an assessment.</summary>
public sealed class NpmOutstandingRentWorkflow(IStallRepository stalls, ICollectorRepository collectors,
    IDailyCollectionRepository days, INpmMonthSettlementService settlement, NpmWholePaymentWorkflow whole,
    NpmPartPaymentWorkflow part, NpmDailyCanonicalPoster poster, NpmRentMonthWorkflow monthly,
    ICurrentUserService user, ICurrentMunicipalityAccessor municipality, IClock clock,
    IEemoCacheInvalidator cache, ITenantContext tenant, ILogger<NpmOutstandingRentWorkflow> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record PeriodPlan(DateOnly Period, NpmWholePaymentQuoteDto Whole, decimal Applied, NpmPartPaymentQuoteDto? Part);
    private sealed record Plan(Stall Stall, NpmOutstandingRentQuote Quote, IReadOnlyList<PeriodPlan> Periods);

    private async Task<bool> Authorized(CancellationToken ct) => user.IsAuthenticated && user.UserId is { } id && id != Guid.Empty
        && municipality.MunicipalityId != Guid.Empty && user.MunicipalityId == municipality.MunicipalityId
        && await NpmSettlementAccess.MaySettleMarketCollectionsAsync(user, collectors, ct);

    public async Task<Result<NpmOutstandingRentQuote>> QuoteAsync(Guid stallId, Guid? contractId = null,
        decimal? amount = null, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<NpmOutstandingRentQuote>.Forbidden();
        var plan = await BuildPlan(stallId, contractId, amount, clock.PhilippineToday, ct);
        return plan.IsSuccess ? Result<NpmOutstandingRentQuote>.Success(plan.Value!.Quote)
            : Result<NpmOutstandingRentQuote>.Failure(plan.Error!, plan.Status);
    }

    public async Task<Result<IReadOnlyList<NpmOutstandingRentAccount>>> AccountsAsync(CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<IReadOnlyList<NpmOutstandingRentAccount>>.Forbidden();
        var accounts = new List<NpmOutstandingRentAccount>();
        foreach (var stall in await stalls.GetNpmStallsWithContractsAsync(ct))
        {
            var sectionName = stall.Section is { } section ? stall.Facility!.SectionLabel(section) ?? "" : stall.CustomSectionName ?? "";
            foreach (var owner in stall.Occupancies(clock.PhilippineToday).Where(x => x.Start <= clock.PhilippineToday).DistinctBy(x => x.Contract.Id))
            {
                var result = await QuoteAsync(stall.Id, owner.Contract.Id, ct: ct);
                if (!result.IsSuccess)
                { accounts.Add(new(stall.Id, owner.Contract.Id, stall.StallNo, owner.Occupant, sectionName, null, result.Error)); continue; }
                var q = result.Value!;
                if (q.TotalOutstanding > 0)
                    accounts.Add(new(stall.Id, q.ContractId, stall.StallNo, q.PayorName,
                        sectionName, q.TotalOutstanding));
            }
        }
        return Result<IReadOnlyList<NpmOutstandingRentAccount>>.Success(accounts);
    }

    /// <summary>
    /// Bounded read for Mobile. A page covers occupancies, not just debtors; a zero-balance
    /// occupant still advances the cursor. Financial amounts are always server-quoted.
    /// The older complete-list endpoint remains for compatibility.
    /// </summary>
    public async Task<Result<NpmOutstandingRentAccountsPage>> AccountsPageAsync(int offset, int pageSize, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<NpmOutstandingRentAccountsPage>.Forbidden();
        if (offset < 0 || pageSize is < 1 or > 5)
            return Result<NpmOutstandingRentAccountsPage>.Failure("Choose a valid outstanding rent page.", ResultStatus.Invalid);

        var today = clock.PhilippineToday;
        var occupancies = (await stalls.GetNpmStallsWithContractsAsync(ct))
            .OrderBy(stall => stall.Section).ThenBy(stall => stall.StallNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stall => stall.Id)
            .SelectMany(stall => stall.Occupancies(today)
                .Where(owner => owner.Start <= today)
                .DistinctBy(owner => owner.Contract.Id)
                .OrderBy(owner => owner.Start)
                .Select(owner => new { Stall = stall, Owner = owner }))
            .ToArray();

        var rows = new List<NpmOutstandingRentAccount>();
        foreach (var entry in occupancies.Skip(offset).Take(pageSize))
        {
            var stall = entry.Stall;
            var owner = entry.Owner;
            var sectionName = stall.Section is { } section
                ? stall.Facility!.SectionLabel(section) ?? "" : stall.CustomSectionName ?? "";
            var result = await BuildPlan(stall.Id, owner.Contract.Id, null, today, ct);
            if (!result.IsSuccess)
                rows.Add(new(stall.Id, owner.Contract.Id, stall.StallNo, owner.Occupant,
                    sectionName, null, result.Error));
            else if (result.Value!.Quote.TotalOutstanding > 0)
                rows.Add(new(stall.Id, owner.Contract.Id, stall.StallNo, result.Value.Quote.PayorName,
                    sectionName, result.Value.Quote.TotalOutstanding));
        }

        var next = offset + pageSize < occupancies.Length ? offset + pageSize : (int?)null;
        return Result<NpmOutstandingRentAccountsPage>.Success(new(rows, next));
    }

    private async Task<Result<Plan>> BuildPlan(Guid stallId, Guid? contractId, decimal? received, DateOnly businessDate, CancellationToken ct)
    {
        if (businessDate != clock.PhilippineToday)
            return Result<Plan>.Failure("RECONCILIATION_REQUIRED: This reviewed business date is no longer current. Confirm the captured payment with the office.", ResultStatus.Conflict);
        if (received is <= 0 || received is { } invalid && decimal.Round(invalid, 2) != invalid)
            return Result<Plan>.Failure("Enter a positive amount with at most two decimal places and review today's balance.");
        var stall = await stalls.GetByIdAsync(stallId, ct);
        if (stall?.Facility is not { Code: FacilityCode.NPM, IsMonthlyRentMarket: false }
            || stall.MunicipalityId != municipality.MunicipalityId) return Result<Plan>.NotFound();
        var windows = stall.Occupancies(businessDate).ToList();
        var owner = contractId is { } selected ? windows.SingleOrDefault(x => x.Contract.Id == selected)
            : windows.Where(x => x.Start <= businessDate).OrderByDescending(x => x.Start).FirstOrDefault();
        if (owner is null || owner.Start > businessDate) return Result<Plan>.Failure("The responsible occupancy is unavailable. Ask the office to review it.", ResultStatus.Conflict);
        var end = new DateOnly(businessDate.Year, businessDate.Month, 1);
        var positions = new List<(DateOnly Period, NpmWholePaymentQuoteDto Quote, decimal Outstanding)>();
        for (var period = new DateOnly(owner.Start.Year, owner.Start.Month, 1); period <= end; period = period.AddMonths(1))
        {
            if (StallOccupancy.AnsweringForMonth(windows, period.Year, period.Month)?.Contract.Id != owner.Contract.Id) continue;
            var quote = await whole.QuoteAsync(stallId, period.Year, period.Month, ct, businessDate);
            if (!quote.IsSuccess) return Result<Plan>.Failure($"{period:MMM yyyy}: {quote.Error}", quote.Status);
            var q = quote.Value!;
            if (q.MonthlyObligation is null || q.Collected is null || q.Credits is null)
                return Result<Plan>.Failure("The original period position is unavailable. Office reconciliation is required.", ResultStatus.Conflict);
            positions.Add((period, q, DomainRules.DailyBilledMonthOutstanding(q.MonthlyObligation.Value, q.Collected.Value, q.Credits.Value)));
        }
        var total = positions.Sum(x => x.Outstanding);
        var amount = received ?? total;
        if (amount > total) return Result<Plan>.Failure("The amount exceeds this occupancy's eligible outstanding rent. No advance or unapplied cash is accepted.");
        var remainder = amount;
        var plan = new List<PeriodPlan>();
        foreach (var position in positions.Where(x => x.Outstanding > 0))
        {
            var applied = Math.Min(remainder, position.Outstanding);
            NpmPartPaymentQuoteDto? partial = null;
            if (applied > 0 && (applied != position.Outstanding || applied != position.Quote.Amount))
            {
                var reviewed = await part.QuoteAsync(stallId, position.Period.Year, position.Period.Month, applied, ct, businessDate);
                if (!reviewed.IsSuccess) return Result<Plan>.Failure($"{position.Period:MMM yyyy}: {reviewed.Error}", reviewed.Status);
                partial = reviewed.Value!;
                if (partial.Outcome != "Applied" || partial.Applied != applied || partial.Unallocated != 0)
                    return Result<Plan>.Failure(partial.Message ?? "The remaining amount cannot be allocated under this period's approved partial-payment rules.", ResultStatus.Conflict);
            }
            plan.Add(new(position.Period, position.Quote, applied, partial));
            remainder -= applied;
        }
        var tokenFacts = new { stallId, contractId = owner.Contract.Id, businessDate, amount,
            periods = positions.Select(x => new { x.Period, x.Outstanding, x.Quote.Amount, x.Quote.QuoteToken }),
            partials = plan.Select(x => new { x.Period, x.Applied, x.Part?.QuoteToken }) };
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenFacts, Json))));
        return Result<Plan>.Success(new(stall, new(stallId, owner.Contract.Id, stall.StallNo, owner.Occupant,
            businessDate, total, amount, total - amount, token, plan.Select(x => new NpmOutstandingRentPeriod(x.Period,
                x.Whole.SettlementSourceKind ?? CollectionSourceKind.DailyCollection,
                positions.Single(p => p.Period == x.Period).Outstanding, x.Applied,
                positions.Single(p => p.Period == x.Period).Outstanding - x.Applied)).ToArray()), plan));
    }

    public async Task<Result<NpmPartPaymentOutcomeDto>> PostAsync(NpmOutstandingRentPayment request, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<NpmPartPaymentOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty || request.ContractId == Guid.Empty || request.Amount <= 0
            || decimal.Round(request.Amount, 2) != request.Amount || string.IsNullOrWhiteSpace(request.QuoteToken))
            return Result<NpmPartPaymentOutcomeDto>.Failure("A reviewed payment with a stable operation ID is required.");
        var intent = poster.NormalizeIntent("NpmOutstandingRent", request.StallId, [], request.BusinessDate,
            JsonSerializer.Serialize(new { request.ContractId, request.Amount, request.QuoteToken }, Json) + NpmReceiptEvidence.IntentSuffix(request.OfficialReceiptNumber));
        if (await poster.FindPriorAsync(request.ClientOperationId, intent, ct) is { } prior) return Outcome(prior);
        await using var transaction = poster.HasActiveTransaction ? null : await poster.BeginSettlementTransactionAsync(ct);
        try
        {
            var proposed = await BuildPlan(request.StallId, request.ContractId, request.Amount, request.BusinessDate, ct);
            if (!proposed.IsSuccess) return Result<NpmPartPaymentOutcomeDto>.Failure(proposed.Error!, proposed.Status);
            var plan = proposed.Value!;
            if (plan.Quote.QuoteToken != request.QuoteToken)
                return Result<NpmPartPaymentOutcomeDto>.Failure("RECONCILIATION_REQUIRED: The balance changed. Review a new allocation before collecting.", ResultStatus.Conflict);
            var charges = new List<NpmDailyCanonicalPoster.Charge>();
            var staged = new List<NpmRentMonthWorkflow.StagedMonthPayment>();
            foreach (var allocation in plan.Periods.Where(x => x.Applied > 0))
            {
                if (allocation.Whole.SettlementSourceKind == CollectionSourceKind.NpmRentMonth)
                {
                    var result = await monthly.StageReviewedPaymentAsync(new(request.ClientOperationId, request.StallId, allocation.Period,
                        request.BusinessDate, allocation.Part is null ? NpmRentPaymentScope.WholeMonth : NpmRentPaymentScope.PartPayment,
                        allocation.Applied, allocation.Part?.QuoteToken ?? allocation.Whole.QuoteToken), ct);
                    if (!result.IsSuccess) return Outcome(poster.DiscardStagedPayment(result.Error!, result.Status));
                    staged.Add(result.Value!);
                }
                else if (allocation.Part is { } partial)
                {
                    foreach (var line in partial.Lines.Where(x => x.Applied > 0))
                    {
                        var day = await days.GetByStallAndDateAsync(request.StallId, line.Day, ct);
                        if (day is null) { day = DailyCollection.Create(request.StallId, line.Day, user.Username!, line.Fee); await days.AddAsync(day, ct); }
                        charges.Add(new(day, line.Applied, PartPayment: true));
                    }
                }
                else
                {
                    var before = (await days.GetByStallAndMonthAsync(request.StallId, allocation.Period.Year, allocation.Period.Month, ct))
                        .Where(x => x.IsPaid).ToDictionary(x => x.Id, x => (x.DailyFee, x.UpdatedAt, x.UpdatedBy));
                    var paid = await settlement.SettleWholeMonthAsync(plan.Stall, allocation.Period.Year, allocation.Period.Month,
                        user.CollectorId, user.Username!, ct, allocation.Applied);
                    charges.AddRange(paid.Select(day => before.TryGetValue(day.Id, out var original)
                        ? new NpmDailyCanonicalPoster.Charge(day, day.DailyFee - original.DailyFee, true, original.UpdatedAt, original.UpdatedBy)
                        : new NpmDailyCanonicalPoster.Charge(day, day.DailyFee)).Where(x => x.Amount > 0));
                }
            }
            if (charges.Sum(x => x.Amount) + staged.Sum(x => x.Line.Amount) != request.Amount)
                return Outcome(poster.DiscardStagedPayment("The allocation could not be reconciled. Nothing was posted.", ResultStatus.Conflict));
            var posted = await poster.PostCombinedAsync(plan.Stall, charges, request.ClientOperationId, intent, request.BusinessDate,
                staged.Select(x => x.Line).ToArray(), staged.Count == 0 ? null : new(municipality.MunicipalityId, request.ClientOperationId, staged.Select(x => x.MonthId).ToHashSet()),
                (_, at) => { foreach (var stage in staged) stage.RecordEvidence(at); }, plan.Quote.PayorName, ct, clock, request.OfficialReceiptNumber);
            if (!posted.IsSuccess) return Outcome(posted);
            if (transaction is not null) await transaction.CommitAsync(ct);
            try { await cache.InvalidateTenantAsync(tenant.TenantCode, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "NPM payment committed; view refresh must retry."); }
            return Outcome(posted);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            return Outcome(poster.DiscardStagedPayment("RECONCILIATION_REQUIRED: The payment was not confirmed. Retry the same operation before starting another.", ResultStatus.Conflict));
        }
    }
    private static Result<NpmPartPaymentOutcomeDto> Outcome(Result<NpmDailyCanonicalPoster.Outcome> result) => result.IsSuccess
        ? Result<NpmPartPaymentOutcomeDto>.Success(new(result.Value!.CollectionId, result.Value.ReferenceCode, result.Value.Amount, result.Value.Existing))
        : Result<NpmPartPaymentOutcomeDto>.Failure(result.Error!, result.Status);
}
