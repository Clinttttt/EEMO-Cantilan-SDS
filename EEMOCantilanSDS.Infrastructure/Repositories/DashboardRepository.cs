using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class DashboardRepository(AppDbContext context) : IDashboardRepository
{
    public async Task<DashboardOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct)
    {
        var facilities = await context.Facilities
            .AsNoTracking()
            .OrderBy(f => f.Code)
            .Select(f => new { f.Id, f.Code, f.Name })
            .ToListAsync(ct);

        // Active, occupied stalls (rate + occupant + facility) — soft-deletes excluded by global filters.
        var stalls = await context.Stalls
            .AsNoTracking()
            .Where(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive))
            .Select(s => new
            {
                s.Id,
                s.FacilityId,
                s.StallNo,
                s.MonthlyRate,
                Occupant = s.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? ""
            })
            .ToListAsync(ct);

        var stallIds = stalls.Select(s => s.Id).ToList();

        // This-month payment per stall (unique per stall/month), loaded as entities for AmountPaid/BalanceDue.
        var monthPayments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.BillingYear == year && p.BillingMonth == month && stallIds.Contains(p.StallId))
            .ToListAsync(ct);
        var paymentByStall = monthPayments.ToDictionary(p => p.StallId);

        var facilityCards = new List<DashboardFacilityDto>();
        var totalPending = 0m;
        foreach (var f in facilities)
        {
            var facStalls = stalls.Where(s => s.FacilityId == f.Id).ToList();
            var collected = 0m;
            var pending = 0m;
            var paid = 0;
            foreach (var s in facStalls)
            {
                if (paymentByStall.TryGetValue(s.Id, out var p))
                {
                    collected += p.AmountPaid;
                    pending += p.BalanceDue;
                    if (p.Status == PaymentStatus.Paid) paid++;
                }
                else
                {
                    pending += s.MonthlyRate; // billed-but-unrecorded month is fully owed
                }
            }
            var total = facStalls.Count;
            totalPending += pending;
            facilityCards.Add(new DashboardFacilityDto(
                f.Code, f.Name, collected,
                UnpaidCount: total - paid,
                TotalVendors: total,
                CollectionRate: total == 0 ? 0 : (int)((double)paid / total * 100)));
        }

        var totalVendors = facilityCards.Sum(c => c.TotalVendors);
        var paidCount = facilityCards.Sum(c => c.TotalVendors - c.UnpaidCount);

        var totalCollectors = await context.CollectorUsers.CountAsync(c => c.IsActive, ct);
        var collectorNames = await context.CollectorUsers
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.FullName ?? "", ct);

        // Recent paid/partial transactions
        var recentEntities = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.PaidAt != null)
            .OrderByDescending(p => p.PaidAt)
            .Take(8)
            .Include(p => p.Stall!).ThenInclude(s => s.Facility)
            .Include(p => p.Stall!).ThenInclude(s => s.Contracts)
            .ToListAsync(ct);

        var recent = recentEntities.Select(p => new DashboardTransactionDto(
            p.ORNumber ?? "",
            p.Stall!.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant ?? "",
            p.Stall.Facility!.Code,
            p.AmountPaid,
            p.CollectorId.HasValue && collectorNames.TryGetValue(p.CollectorId.Value, out var n) ? n : "",
            p.PaidAt!.Value)).ToList();

        // Delinquents = recorded unpaid/partial months in the last 12, per stall.
        var since = new DateOnly(year, month, 1).AddMonths(-11);
        var unpaidWindow = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId)
                && p.Status != PaymentStatus.Paid
                && (p.BillingYear > since.Year || (p.BillingYear == since.Year && p.BillingMonth >= since.Month)))
            .ToListAsync(ct);

        var facilityCodeById = facilities.ToDictionary(f => f.Id, f => f.Code);
        var stallById = stalls.ToDictionary(s => s.Id);
        var delinquents = unpaidWindow
            .GroupBy(p => p.StallId)
            .Where(g => stallById.ContainsKey(g.Key))
            .Select(g =>
            {
                var s = stallById[g.Key];
                return new DashboardDelinquentDto(s.Occupant, s.StallNo, facilityCodeById[s.FacilityId], g.Count(), g.Sum(p => p.BalanceDue));
            })
            .OrderByDescending(d => d.MonthsUnpaid).ThenByDescending(d => d.Balance)
            .Take(5)
            .ToList();

        return new DashboardOverviewDto(
            TotalCollected: facilityCards.Sum(c => c.Collected),
            TotalPending: totalPending,
            UnpaidCount: facilityCards.Sum(c => c.UnpaidCount),
            PaidCount: paidCount,
            CollectionRate: totalVendors == 0 ? 0 : (int)((double)paidCount / totalVendors * 100),
            ActiveFacilitiesCount: facilityCards.Count(c => c.Collected > 0),
            TotalCollectors: totalCollectors,
            Facilities: facilityCards,
            RecentTransactions: recent,
            DelinquentVendors: delinquents);
    }
}
