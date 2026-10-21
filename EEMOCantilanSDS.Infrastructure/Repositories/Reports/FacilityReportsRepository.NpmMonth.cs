using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public partial class FacilityReportsRepository
{
    // Evidence is batched per assessment month/cutoff and cached only within this read request.
    private readonly Dictionary<(Guid Stall, DateOnly Period, DateOnly Cutoff), NpmRentMonthReportPosition?> _monthReports = [];
    private readonly Dictionary<(Guid Facility, DateOnly From, DateOnly To, DateOnly Cutoff), IReadOnlyList<NpmRentMonthCashMovement>> _monthCash = [];
    private DateOnly _monthReportCutoff;
    private HashSet<(Guid Stall, DateOnly Period)>? _activatedMonths;

    private async Task<NpmRentMonthReportPosition?> MonthReportAsync(Guid stall, DateOnly period, DateOnly cutoff, CancellationToken ct)
    {
        if (_npmRentMonths is null) return null;
        _activatedMonths ??= (await _context.NpmRentMonths.AsNoTracking().Where(x =>
            x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated || _context.CollectionAllocations.Any(a =>
                a.SourceKind == CollectionSourceKind.NpmRentMonth && a.MunicipalityId == x.MunicipalityId && a.SourceId == x.Id))
            .Select(x => new { x.StallId, x.PeriodStart }).ToListAsync(ct)).Select(x => (x.StallId, x.PeriodStart)).ToHashSet();
        if (!_activatedMonths.Contains((stall, period))) return null;
        cutoff = cutoff > _clock.PhilippineToday ? _clock.PhilippineToday : cutoff;
        var key = (stall, period, cutoff);
        if (!_monthReports.TryGetValue(key, out var result))
        {
            var ids = _activatedMonths.Where(x => x.Period == period).Select(x => x.Stall).ToArray();
            foreach (var row in await _npmRentMonths.ReadReportPositionsAsync(ids, period, cutoff, ct))
                _monthReports[(row.Key, period, cutoff)] = row.Value;
            result = _monthReports.GetValueOrDefault(key);
        }
        return result;
    }

    private async Task<IReadOnlyList<NpmRentMonthCashMovement>> MonthCashAsync(Guid facility, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (_npmRentMonths is null) return [];
        var key = (facility, from, to, _monthReportCutoff);
        if (!_monthCash.TryGetValue(key, out var result))
            _monthCash[key] = result = await _npmRentMonths.ReadCashMovementsAsync(facility, from, to, ct, _monthReportCutoff);
        return result;
    }

    private async Task<(decimal BillDelta, decimal PaidDelta)> MonthComplianceDeltaAsync(Stall stall, DateOnly from, DateOnly to,
        IReadOnlySet<DateOnly>? absent, CancellationToken ct, DateOnly? balanceAsOf = null)
    {
        if (_npmRentMonths is null) return (0, 0);
        decimal bill = 0, paid = 0;
        var end = to > _clock.PhilippineToday ? _clock.PhilippineToday : to;
        for (var period = new DateOnly(from.Year, from.Month, 1); period <= end; period = period.AddMonths(1))
        {
            var source = await MonthReportAsync(stall.Id, period, balanceAsOf ?? end, ct);
            if (source is null) continue;
            // A monthly payment carries no invented allocation to a selected week. Require a month-based view.
            var owner = StallOccupancy.AnsweringForMonth(stall.Occupancies(_clock.PhilippineToday), period.Year, period.Month);
            if (owner is not null && owner.Contract.Id != source.Position.ContractId)
                throw new InvalidOperationException("Reconcile the historical responsible contract before reporting this month.");
            if (from > period && (owner is null || from > owner.Start))
                throw new NpmMonthReportScopeException();
            var periodEnd = period.AddMonths(1).AddDays(-1);
            if (periodEnd > end) periodEnd = end;
            var oldPaid = await _context.DailyCollections.AsNoTracking().Where(x => x.StallId == stall.Id
                && x.CollectionDate >= period && x.CollectionDate <= periodEnd).SumAsync(x => x.IsPaid ? x.DailyFee : x.CanonicalSettledAmount, ct);
            bill += source.EarnedLiability - CalculateNpmDailyObligation(stall, period, periodEnd, absent);
            paid += source.Position.LegacyCash + source.Position.CanonicalCash - oldPaid;
        }
        return (bill, paid);
    }

    private async Task<decimal> MonthExpectedDeltaAsync(IEnumerable<Stall> stalls, DateOnly from, DateOnly to, CancellationToken ct)
    {
        decimal delta = 0;
        foreach (var stall in stalls) delta += (await MonthComplianceDeltaAsync(stall, from, to, null, ct)).BillDelta;
        return delta;
    }
}
