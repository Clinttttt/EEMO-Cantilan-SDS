using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class DashboardRepository(AppDbContext context, IFacilityReportsRepository facilityReports) : IDashboardRepository
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

        // ── Context-aware revenue for non-stall facilities (their data lives in their own tables) ──
        // SLH — per-head slaughter transactions (all are receipted/paid on creation).
        var slaughter = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(t => t.TransactionDate.Year == year && t.TransactionDate.Month == month)
            .Select(t => new { t.OwnerName, Amount = t.RatePerHead * t.NumberOfHeads })
            .ToListAsync(ct);

        // TRM — terminal trips (UTC instants → Philippine-time month range), all paid.
        var (trmStart, trmEnd) = PhilippineTime.MonthUtcRange(year, month);
        var trips = await context.TrmTrips
            .AsNoTracking()
            .Where(t => t.RecordedAt >= trmStart && t.RecordedAt < trmEnd)
            .Select(t => new { t.TransporterId, t.Fee })
            .ToListAsync(ct);

        // TPM — Friday market attendance (paid/unpaid per vendor per market day).
        var tpm = await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.MarketDate.Year == year && a.MarketDate.Month == month)
            .Select(a => new { a.VendorId, a.IsPaid, a.Fee })
            .ToListAsync(ct);

        var stallCodes = new[] { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE };

        var facilityCards = new List<DashboardFacilityDto>();
        var totalPending = 0m;
        foreach (var f in facilities)
        {
            // Slaughterhouse — owners served, total fees collected.
            if (f.Code == FacilityCode.SLH)
            {
                var collectedSlh = slaughter.Sum(x => x.Amount);
                var owners = slaughter.Select(x => x.OwnerName).Distinct().Count();
                facilityCards.Add(new DashboardFacilityDto(
                    f.Code, f.Name, collectedSlh,
                    UnpaidCount: 0, PaidCount: slaughter.Count, PartialCount: 0,
                    TotalVendors: owners, CollectionRate: slaughter.Count > 0 ? 100 : 0));
                continue;
            }
            // Transport Terminal — trips dispatched, fees collected.
            if (f.Code == FacilityCode.TRM)
            {
                var collectedTrm = trips.Sum(x => x.Fee);
                var transporters = trips.Select(x => x.TransporterId).Distinct().Count();
                facilityCards.Add(new DashboardFacilityDto(
                    f.Code, f.Name, collectedTrm,
                    UnpaidCount: 0, PaidCount: trips.Count, PartialCount: 0,
                    TotalVendors: transporters, CollectionRate: trips.Count > 0 ? 100 : 0));
                continue;
            }
            // Tabo-an Market — Friday vendor attendance, paid vs unpaid.
            if (f.Code == FacilityCode.TPM)
            {
                var paidTpm = tpm.Count(x => x.IsPaid);
                var collectedTpm = tpm.Where(x => x.IsPaid).Sum(x => x.Fee);
                var pendingTpm = tpm.Where(x => !x.IsPaid).Sum(x => x.Fee);
                var vendorsTpm = tpm.Select(x => x.VendorId).Distinct().Count();
                totalPending += pendingTpm;
                facilityCards.Add(new DashboardFacilityDto(
                    f.Code, f.Name, collectedTpm,
                    UnpaidCount: tpm.Count - paidTpm, PaidCount: paidTpm, PartialCount: 0,
                    TotalVendors: vendorsTpm,
                    CollectionRate: tpm.Count == 0 ? 0 : (int)((double)paidTpm / tpm.Count * 100)));
                continue;
            }

            // Stall facilities (NPM, TCC, NCC, BBQ, ICE) — delegate to the canonical, daily-aware
            // facility-reports aggregation so the dashboard matches the facility page and reports.
            var snap = await facilityReports.GetFacilitySnapshotAsync(f.Code, year, month, ct);
            totalPending += snap.Pending;
            facilityCards.Add(new DashboardFacilityDto(
                f.Code, f.Name, snap.Collected,
                UnpaidCount: snap.UnpaidCount,
                PaidCount: snap.PaidCount,
                PartialCount: snap.PartialCount,
                TotalVendors: snap.OccupiedStalls,
                CollectionRate: snap.CollectionRate));
        }

        // Hero vendor KPIs stay stall-based (vendor = stallholder); SLH/TRM/TPM counts are
        // contextual participants and would distort a "collection rate of vendors".
        var stallCards = facilityCards.Where(c => stallCodes.Contains(c.Code)).ToList();
        var totalVendors = stallCards.Sum(c => c.TotalVendors);
        var paidCount = stallCards.Sum(c => c.PaidCount);
        var heroUnpaidCount = stallCards.Sum(c => c.UnpaidCount);

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

        // Delinquents = recorded unpaid/partial months in the rolling window, EXCLUDING the current
        // billing month (the current period isn't "overdue" yet — only past unpaid months count).
        var since = new DateOnly(year, month, 1).AddMonths(-11);
        var unpaidWindow = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId)
                && p.Status != PaymentStatus.Paid
                && (p.BillingYear > since.Year || (p.BillingYear == since.Year && p.BillingMonth >= since.Month))
                && (p.BillingYear < year || (p.BillingYear == year && p.BillingMonth < month)))
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
            UnpaidCount: heroUnpaidCount,
            PaidCount: paidCount,
            CollectionRate: totalVendors == 0 ? 0 : (int)((double)paidCount / totalVendors * 100),
            ActiveFacilitiesCount: facilityCards.Count(c => c.Collected > 0),
            TotalCollectors: totalCollectors,
            Facilities: facilityCards,
            RecentTransactions: recent,
            DelinquentVendors: delinquents);
    }
}
