using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>Preserves legacy months; an activated source must never fall back to day-based financial authority.</summary>
public sealed class NpmMonthSettlementBridge(NpmMonthSettlementService legacy, NpmRentMonthWorkflow monthly, IClock clock)
    : INpmMonthSettlementService
{
    private Task<bool> Activated(Stall stall, int year, int month, CancellationToken ct) =>
        monthly.IsMonthActivatedAsync(stall.Id, new(year, month, 1), ct);

    public async Task<NpmMonthPayable> ComputePayableAsync(Stall stall, int year, int month, CancellationToken ct)
    {
        if (!await Activated(stall, year, month, ct)) return await legacy.ComputePayableAsync(stall, year, month, ct);
        return await monthly.ReadEarnedPositionAsync(stall.Id, new(year, month, 1), ct)
            ?? throw new InvalidOperationException("Monthly balance is unavailable.");
    }

    public async Task<NpmMonthPayable> ComputeWholeMonthPayableAsync(Stall stall, int year, int month, CancellationToken ct)
    {
        if (!await Activated(stall, year, month, ct)) return await legacy.ComputeWholeMonthPayableAsync(stall, year, month, ct);
        var position = await monthly.ReadPositionAsync(stall.Id, new(year, month, 1), ct)
            ?? throw new InvalidOperationException("Monthly balance is unavailable.");
        return new(0, position.Outstanding, 0, position.Assessment, position.LegacyCash + position.CanonicalCash, position.Credits, position.MonthId);
    }

    private async Task<IReadOnlyList<(DateOnly Day, decimal Amount)>> DailyPlan(Stall stall, int year, int month, CancellationToken ct)
    {
        var position = await monthly.ReadPositionAsync(stall.Id, new(year, month, 1), ct)
            ?? throw new InvalidOperationException("Activated month source is missing.");
        var budget = position.Outstanding;
        var result = new List<(DateOnly, decimal)>();
        foreach (var day in await legacy.GetPayableDaysAsync(stall, year, month, ct))
        {
            if (budget <= 0) break;
            var quote = await monthly.QuoteAsync(stall.Id, new(year, month, 1), clock.PhilippineToday,
                NpmRentPaymentScope.DailyInstallment, ct, installmentDate: day);
            if (!quote.IsSuccess) throw new InvalidOperationException(quote.Error ?? "Daily installment is unavailable.");
            var amount = Math.Min(budget, quote.Value!.EligibleAmount);
            if (amount <= 0) continue;
            result.Add((day, amount));
            budget -= amount;
        }
        return result;
    }

    public async Task<IReadOnlyList<DateOnly>> GetPayableDaysAsync(Stall stall, int year, int month, CancellationToken ct) =>
        !await Activated(stall, year, month, ct) ? await legacy.GetPayableDaysAsync(stall, year, month, ct)
            : (await DailyPlan(stall, year, month, ct)).Select(x => x.Day).ToArray();

    public async Task<NpmMonthPayable> ComputePayableForDaysAsync(Stall stall, int year, int month, int dayCount, CancellationToken ct)
    {
        if (!await Activated(stall, year, month, ct)) return await legacy.ComputePayableForDaysAsync(stall, year, month, dayCount, ct);
        if (dayCount < 1) throw new ArgumentOutOfRangeException(nameof(dayCount));
        var plan = (await DailyPlan(stall, year, month, ct)).Take(dayCount).ToArray();
        return new(plan.Length, plan.Sum(x => x.Amount));
    }

    public async Task<IReadOnlyList<DateOnly>> GetWholeMonthPayableDaysAsync(Stall stall, int year, int month, CancellationToken ct)
    {
        if (!await Activated(stall, year, month, ct)) return await legacy.GetWholeMonthPayableDaysAsync(stall, year, month, ct);
        await RejectLegacyWriter(stall, year, month, ct);
        return Array.Empty<DateOnly>();
    }

    private async Task RejectLegacyWriter(Stall stall, int year, int month, CancellationToken ct)
    {
        if (await Activated(stall, year, month, ct))
            throw new InvalidOperationException("Use the reviewed month-source quote and canonical posting workflow; daily rows cannot represent this payment.");
    }

    public async Task<IReadOnlyList<DailyCollection>> SettleUnpaidDaysAsync(Stall stall, int year, int month, Guid? collectorId, string recordedBy, CancellationToken ct, decimal? maxAmount = null)
    {
        await RejectLegacyWriter(stall, year, month, ct);
        return await legacy.SettleUnpaidDaysAsync(stall, year, month, collectorId, recordedBy, ct, maxAmount);
    }

    public async Task<IReadOnlyList<DailyCollection>> SettleWholeMonthAsync(Stall stall, int year, int month, Guid? collectorId, string recordedBy, CancellationToken ct, decimal? maxAmount = null)
    {
        await RejectLegacyWriter(stall, year, month, ct);
        return await legacy.SettleWholeMonthAsync(stall, year, month, collectorId, recordedBy, ct, maxAmount);
    }

    public async Task<NpmPartPaymentPlan> PlanPartPaymentAsync(Stall stall, int year, int month, decimal amount, CancellationToken ct)
    {
        await RejectLegacyWriter(stall, year, month, ct);
        return await legacy.PlanPartPaymentAsync(stall, year, month, amount, ct);
    }

    public async Task<NpmFishDayQuote> QuoteFishDayAsync(Stall stall, DateOnly day, decimal declaredKilos, CancellationToken ct)
    {
        if (await Activated(stall, day.Year, day.Month, ct))
            return NpmFishDayQuote.NotPayable("Use separate reviewed rent and weighing sources for this activated month.");
        return await legacy.QuoteFishDayAsync(stall, day, declaredKilos, ct);
    }

    public async Task<DailyCollection?> SettleFishDayAsync(Stall stall, DateOnly day, decimal declaredKilos, string recordedBy, CancellationToken ct)
    {
        await RejectLegacyWriter(stall, day.Year, day.Month, ct);
        return await legacy.SettleFishDayAsync(stall, day, declaredKilos, recordedBy, ct);
    }
}
