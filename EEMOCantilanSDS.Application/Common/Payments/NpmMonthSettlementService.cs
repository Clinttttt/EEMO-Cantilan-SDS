using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
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
    IFeeRateResolver feeRateResolver) : INpmMonthSettlementService
{
    public async Task<NpmMonthPayable> ComputePayableAsync(Stall stall, int year, int month, CancellationToken ct)
    {
        var days = await ResolvePayableDaysAsync(stall, year, month, ct);
        return new NpmMonthPayable(days.Count, days.Sum(d => d.Fee));
    }

    public async Task<IReadOnlyList<DailyCollection>> SettleUnpaidDaysAsync(
        Stall stall, int year, int month, Guid? collectorId, string recordedBy, CancellationToken ct)
    {
        var payable = await ResolvePayableDaysAsync(stall, year, month, ct);
        if (payable.Count == 0)
            return Array.Empty<DailyCollection>();

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(stall.Id, year, month, ct))
            .ToDictionary(dc => dc.CollectionDate);

        var settled = new List<DailyCollection>(payable.Count);
        foreach (var (day, fee) in payable)
        {
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
        return settled;
    }

    // The day-set + per-day fee. Kept private so both entry points compute it identically.
    private async Task<List<(DateOnly Day, decimal Fee)>> ResolvePayableDaysAsync(
        Stall stall, int year, int month, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var today = PhilippineTime.Today;
        var contract = stall.Contracts.FirstOrDefault(c => c.IsActive);

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(stall.Id, year, month, ct))
            .ToDictionary(dc => dc.CollectionDate);
        var closedDates = (await marketClosureRepository.GetByMonthAsync(year, month, ct))
            .Select(c => c.ClosureDate)
            .ToHashSet();
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);

        var days = new List<(DateOnly, decimal)>();
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;                                     // never settle future days
            if (contract is null || !(contract.EffectivityDate <= day && day <= contract.ExpiryDate))
                continue;                                               // not under an effective contract
            if (closedDates.Contains(day))
                continue;                                               // facility-wide closure — nothing owed
            existing.TryGetValue(day, out var dc);
            if (dc is not null && (dc.IsPaid || dc.IsAbsent))
                continue;                                               // already collected or excused
            days.Add((day, snapshot.Resolve(FeeRateKey.NpmDailyStall, day)));
        }
        return days;
    }
}

/// <summary>The count of settleable days and their total base fee for an NPM stall's month.</summary>
public readonly record struct NpmMonthPayable(int Days, decimal Amount);
