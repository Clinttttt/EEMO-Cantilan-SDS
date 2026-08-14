using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Single source of the NPM whole-month daily-fee settlement rule, shared by the staff
/// <c>SettleNpmMonth</c> command and the online-payment path so the amount charged online can never
/// diverge from the days actually settled. A "payable day" is one that is: within the requested month,
/// not in the future, covered by the stall's active contract term, not a facility-wide market closure,
/// and not already Paid or Absent. Base ₱30 daily fee only (fish ₱/kg is weighed at the stall; utilities
/// are billed separately) — matching the existing <c>SettleNpmMonth</c> behaviour exactly.
/// </summary>
public sealed class NpmMonthSettlementService(
    IDailyCollectionRepository dailyCollectionRepository,
    INpmMarketClosureRepository marketClosureRepository,
    IFeeRateResolver feeRateResolver, IClock clock) : INpmMonthSettlementService
{
    public async Task<NpmMonthPayable> ComputePayableAsync(Stall stall, int year, int month, CancellationToken ct)
    {
        var (days, remaining, adjustment, _) = await ResolveMonthAsync(stall, year, month, ct);

        // Never ask for more than the month still owes, and never less: the month is let for a rent, so a 31-day
        // month is quoted ₱900 rather than an extra installment, and a closed February is quoted its full ₱900 —
        // twenty-eight installments plus the ₱60 month-end adjustment. The figure charged is the balance the payor
        // was shown, and the day COUNT is trimmed to the installments that amount covers, so the quote and what
        // settlement marks can never disagree.
        var quotedDays = 0;
        var amount = 0m;
        foreach (var (_, fee) in days)
        {
            if (amount + fee > remaining - adjustment) break;
            amount += fee;
            quotedDays++;
        }

        return new NpmMonthPayable(quotedDays, amount + adjustment, adjustment);
    }

    public async Task<IReadOnlyList<DateOnly>> GetPayableDaysAsync(Stall stall, int year, int month, CancellationToken ct)
    {
        // Every uncollected day, uncapped: the fish section settles day by day (each day's total depends on that
        // day's kilos), which is the day-to-day path and not a single charge for the month.
        var (days, _, _, _) = await ResolveMonthAsync(stall, year, month, ct);
        return days.Select(d => d.Day).ToList();
    }

    public async Task<IReadOnlyList<DailyCollection>> SettleUnpaidDaysAsync(
        Stall stall, int year, int month, Guid? collectorId, string recordedBy, CancellationToken ct, decimal? maxAmount = null)
    {
        var (payable, remaining, adjustment, lastCollected) = await ResolveMonthAsync(stall, year, month, ct);

        if (payable.Count == 0)
        {
            // Every day of a closed short month already collected, and the month still short of its rent: the
            // difference rides on the last installment taken. Without this, a payor who paid every day of February
            // at the stall would read as owing that difference for ever — no day remains to collect it on.
            if (adjustment > 0m && lastCollected is not null && (maxAmount is null || maxAmount >= adjustment))
            {
                lastCollected.AddMonthEndAdjustment(adjustment, recordedBy);
                return new[] { lastCollected };
            }

            return Array.Empty<DailyCollection>();
        }

        // Settling "the month" settles what the month owes: its rent, and never more than the money actually
        // captured. The office can still mark a further day in the daily calendar — that is the day-to-day path,
        // and the day's fee is revenue beyond the rent rather than an arrear.
        var cap = maxAmount is { } captured && captured < remaining ? captured : remaining;
        // The month-end adjustment rides on the last installment settled, so the month's ledger reaches its rent
        // exactly. It is only ever non-zero once the month has closed.
        var installmentCap = cap - adjustment;

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(stall.Id, year, month, ct))
            .ToDictionary(dc => dc.CollectionDate);

        var settled = new List<DailyCollection>(payable.Count);
        var accumulated = 0m;
        foreach (var (day, fee) in payable)
        {
            // Stop once the next installment would exceed what this settlement may collect.
            if (accumulated + fee > installmentCap)
                break;
            accumulated += fee;

            existing.TryGetValue(day, out var dc);
            if (dc is null)
            {
                dc = DailyCollection.Create(stall.Id, day, recordedBy, fee);
                dc.MarkPaid(orNumber: string.Empty, collectorId: collectorId, fishKilos: null, updatedBy: recordedBy);
                await dailyCollectionRepository.AddAsync(dc, ct);
            }
            else
            {
                dc.MarkPaid(orNumber: string.Empty, collectorId: collectorId, fishKilos: null, updatedBy: recordedBy);
            }
            settled.Add(dc);
        }

        // The month closed short of its rent, so the difference is collected with the last installment: the month's
        // obligation is met in full and nothing is left to read as arrears.
        if (adjustment > 0m && settled.Count > 0 && accumulated + adjustment <= cap)
            settled[^1].AddMonthEndAdjustment(adjustment, recordedBy);

        return settled;
    }

    /// <inheritdoc />
    public async Task<NpmFishDayQuote> QuoteFishDayAsync(Stall stall, DateOnly day, decimal declaredKilos, CancellationToken ct)
    {
        if (declaredKilos < 0m)
            return NpmFishDayQuote.NotPayable("Declared kilos can't be negative.");
        if (day > clock.PhilippineToday)
            return NpmFishDayQuote.NotPayable("That day hasn't happened yet.");

        var contract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (contract is null || !(contract.EffectivityDate <= day && day <= contract.ExpiryDate))
            return NpmFishDayQuote.NotPayable("That day isn't covered by an active contract for this stall.");

        var closed = await marketClosureRepository.GetByMonthAsync(day.Year, day.Month, ct);
        if (closed.Any(c => c.ClosureDate == day))
            return NpmFishDayQuote.NotPayable("The market was closed that day — nothing is owed.");

        var existing = await dailyCollectionRepository.GetByStallAndDateAsync(stall.Id, day, ct);
        if (existing is not null && (existing.IsPaid || existing.IsAbsent))
            return NpmFishDayQuote.NotPayable("That day is already collected or excused.");

        // Tenant-aware: resolve BOTH the base fee and the fish ₱/kg from the current municipality's
        // snapshot as-of the day (custom LGUs use their configured rates; Cantilan the ordinance constant).
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var baseFee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, day));
        var fishRate = snapshot.Resolve(FeeRateKey.NpmFishPerKilo, day);
        return NpmFishDayQuote.Payable(baseFee + declaredKilos * fishRate, baseFee, fishRate);
    }

    /// <inheritdoc />
    public async Task<DailyCollection?> SettleFishDayAsync(
        Stall stall, DateOnly day, decimal declaredKilos, string recordedBy, CancellationToken ct)
    {
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var baseFee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, day));

        var dc = await dailyCollectionRepository.GetByStallAndDateAsync(stall.Id, day, ct);
        if (dc is null)
        {
            dc = DailyCollection.Create(stall.Id, day, recordedBy, baseFee);
            dc.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: declaredKilos, updatedBy: recordedBy);
            await dailyCollectionRepository.AddAsync(dc, ct);
        }
        else if (!dc.IsPaid && !dc.IsAbsent)
        {
            dc.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: declaredKilos, updatedBy: recordedBy);
        }
        // else: already collected/excused in person between checkout and settlement — leave it untouched
        // (the captured money is still recorded on the transaction for audit/refund).
        return dc;
    }

    // The day-set, and what the month still owes. Kept private so the quote and the settlement can never compute
    // them differently: one walk of the month answers both.
    private async Task<(List<(DateOnly Day, decimal Fee)> Days, decimal Remaining, decimal Adjustment, DailyCollection? LastCollected)> ResolveMonthAsync(
        Stall stall, int year, int month, CancellationToken ct)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, daysInMonth);
        var today = clock.PhilippineToday;

        // The occupancy that answers for this month — the same rule the staff path and every read use — so a month
        // on a stall since re-let is settled for the lessee who held it then, not for whoever holds it now.
        var occupancy = StallOccupancy.AnsweringForMonth(stall.Occupancies(today), year, month);

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(stall.Id, year, month, ct))
            .ToDictionary(dc => dc.CollectionDate);
        var closedDates = (await marketClosureRepository.GetByMonthAsync(year, month, ct))
            .Select(c => c.ClosureDate)
            .ToHashSet();
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);

        var days = new List<(DateOnly Day, decimal Fee)>();
        var daysHeld = 0;
        var daysForgiven = 0;
        var collected = 0m;
        DailyCollection? lastCollected = null;

        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;                                     // never settle future days
            if (occupancy is null || day < occupancy.Start || day > occupancy.BillableEnd)
                continue;                                               // not this lessee's day to answer for

            daysHeld++;

            if (closedDates.Contains(day))
            {
                daysForgiven++;                                         // facility-wide closure — nothing owed
                continue;
            }

            existing.TryGetValue(day, out var dc);
            if (dc is not null && dc.IsAbsent)
            {
                daysForgiven++;                                         // excused — the payor owes nothing for it
                continue;
            }

            if (dc is not null && dc.IsPaid)
            {
                collected += dc.DailyFee;                               // already in — reduces what is left to ask
                lastCollected = dc;                                     // where a month-end adjustment can land
                continue;
            }

            days.Add((day, stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, day))));
        }

        // The month's own obligation: its contractual rent when held in full, whatever the calendar gave it, less
        // the days nothing is owed for. The installments below settle against it.
        var rateDay = today < monthEnd ? today : monthEnd;
        var monthFee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, rateDay));
        var monthRent = stall.ResolveMonthlyRent(
            snapshot.Resolve(FeeRateKey.NpmDailyStall, rateDay),
            snapshot.Resolve(FeeRateKey.NpmMonthlyStall, rateDay));
        var obligation = DomainRules.DailyBilledMonthObligation(monthFee, monthRent, daysInMonth, daysHeld);
        var credit = DomainRules.DailyBilledMonthCredit(monthFee, obligation, daysHeld, daysForgiven);
        var remaining = DomainRules.DailyBilledMonthOutstanding(obligation, collected, credit);

        // A month whose installments cannot reach its rent — February's 28 days at ₱30 fall ₱60 short of ₱900 —
        // carries the difference as a month-end balance adjustment. It becomes collectible only once the month has
        // closed: before its due date it is not yet owed, so it is never quoted, never settled and never arrears.
        var dueDatePassed = today > monthEnd;
        var installments = days.Sum(d => d.Fee);
        var adjustment = dueDatePassed && remaining > installments ? remaining - installments : 0m;

        return (days, remaining, adjustment, lastCollected);
    }
}

/// <summary>The count of settleable days and their total base fee for an NPM stall's month.</summary>
/// <summary>
/// The installments still settleable for an NPM stall's month, and what they come to: <paramref name="Days"/>
/// installments plus, once the month has closed, its <paramref name="Adjustment"/> — the part of the month's rent
/// its calendar could not reach in daily installments. <paramref name="Amount"/> is the whole figure charged.
/// </summary>
public readonly record struct NpmMonthPayable(int Days, decimal Amount, decimal Adjustment = 0m);

/// <summary>
/// A quote for one online-declarable NPM fish day: whether it's payable (with a reason if not), the total
/// due (base + declared kilos × fish rate), and the resolved base fee / fish ₱-per-kg used (for display).
/// </summary>
public readonly record struct NpmFishDayQuote(bool IsPayable, string? Error, decimal Amount, decimal BaseFee, decimal FishRatePerKilo)
{
    public static NpmFishDayQuote Payable(decimal amount, decimal baseFee, decimal fishRatePerKilo) =>
        new(true, null, amount, baseFee, fishRatePerKilo);
    public static NpmFishDayQuote NotPayable(string error) => new(false, error, 0m, 0m, 0m);
}

