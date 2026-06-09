using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
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

        // Recent paid/partial transactions across ALL facilities (newest first).
        // Each facility stores its collections in its own table, so gather the latest from each
        // source then merge — otherwise NPM-daily, SLH, TRM and TPM never appear here.
        const int recentTake = 8;

        var recentPaymentEntities = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.PaidAt != null)
            .OrderByDescending(p => p.PaidAt)
            .Take(recentTake)
            .Include(p => p.Stall!).ThenInclude(s => s.Facility)
            .Include(p => p.Stall!).ThenInclude(s => s.Contracts)
            .ToListAsync(ct);

        var recentRows = recentPaymentEntities.Select(p => new RecentRow(
            p.ORNumber ?? "",
            p.Stall!.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant ?? "",
            p.Stall.Facility!.Code,
            p.AmountPaid,
            p.CollectorId,
            p.PaidAt!.Value)).ToList();

        recentRows.AddRange((await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.IsPaid)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Take(recentTake)
            .Select(d => new
            {
                d.ORNumber,
                Occupant = d.Stall!.Contracts.Where(c => c.IsActive && !c.IsDeleted).Select(c => c.ActualOccupant).FirstOrDefault(),
                Code = d.Stall.Facility!.Code,
                d.DailyFee,
                d.FishKilos,
                d.CollectorId,
                At = d.UpdatedAt ?? d.CreatedAt
            })
            .ToListAsync(ct))
            .Select(d => new RecentRow(d.ORNumber ?? "", d.Occupant ?? "", d.Code,
                d.DailyFee + (d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo, d.CollectorId, At: d.At)));

        recentRows.AddRange((await context.SlaughterTransactions
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(recentTake)
            .Select(s => new { s.ORNumber, s.OwnerName, Amount = s.RatePerHead * s.NumberOfHeads, s.CollectorId, At = s.UpdatedAt ?? s.CreatedAt })
            .ToListAsync(ct))
            .Select(s => new RecentRow(s.ORNumber ?? "", s.OwnerName, FacilityCode.SLH, s.Amount, s.CollectorId, s.At)));

        recentRows.AddRange((await context.TrmTrips
            .AsNoTracking()
            .OrderByDescending(t => t.RecordedAt)
            .Take(recentTake)
            .Select(t => new { t.ORNumber, t.DriverName, t.Fee, t.CollectorId, t.RecordedAt })
            .ToListAsync(ct))
            .Select(t => new RecentRow(t.ORNumber ?? "", t.DriverName, FacilityCode.TRM, t.Fee, t.CollectorId, t.RecordedAt)));

        recentRows.AddRange((await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.IsPaid)
            .OrderByDescending(a => a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt)
            .Take(recentTake)
            .Select(a => new { a.ORNumber, Vendor = a.Vendor!.VendorName, a.Fee, a.CollectorId, At = a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt })
            .ToListAsync(ct))
            .Select(a => new RecentRow(a.ORNumber ?? "", a.Vendor, FacilityCode.TPM, a.Fee, a.CollectorId, a.At)));

        var recent = recentRows
            .OrderByDescending(r => r.At)
            .Take(recentTake)
            .Select(r => new DashboardTransactionDto(
                r.OR,
                r.Payor,
                r.Facility,
                r.Amount,
                r.CollectorId.HasValue && collectorNames.TryGetValue(r.CollectorId.Value, out var n) ? n : "",
                r.At))
            .ToList();

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

    // Normalized recent-transaction row used to merge the latest collections across all facilities.
    private sealed record RecentRow(
        string OR, string Payor, FacilityCode Facility, decimal Amount, Guid? CollectorId, DateTime At);
}
