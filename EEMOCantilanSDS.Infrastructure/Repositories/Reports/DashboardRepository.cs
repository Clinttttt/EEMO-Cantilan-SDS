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
                var transporters = trips.Where(x => x.TransporterId.HasValue).Select(x => x.TransporterId).Distinct().Count();
                facilityCards.Add(new DashboardFacilityDto(
                    f.Code, f.Name, collectedTrm,
                    UnpaidCount: 0, PaidCount: trips.Count, PartialCount: 0,
                    TotalVendors: transporters, CollectionRate: trips.Count > 0 ? 100 : 0));
                continue;
            }
            // Tabo-an Market — Friday vendor attendance. Paid on service (pay to participate): only the
            // paid attendances are revenue, and unpaid attendances are NOT a recurring balance — so TPM
            // contributes no pending and shows a 100% rate, consistent with the Financial Reports page.
            if (f.Code == FacilityCode.TPM)
            {
                var paidTpm = tpm.Count(x => x.IsPaid);
                var collectedTpm = tpm.Where(x => x.IsPaid).Sum(x => x.Fee);
                var vendorsTpm = tpm.Where(x => x.IsPaid).Select(x => x.VendorId).Distinct().Count();
                facilityCards.Add(new DashboardFacilityDto(
                    f.Code, f.Name, collectedTpm,
                    UnpaidCount: 0, PaidCount: paidTpm, PartialCount: 0,
                    TotalVendors: vendorsTpm,
                    CollectionRate: paidTpm > 0 ? 100 : 0));
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
        var paidCount = stallCards.Sum(c => c.PaidCount);
        var heroUnpaidCount = stallCards.Sum(c => c.UnpaidCount);

        var totalCollectors = await context.CollectorUsers.CountAsync(c => c.IsActive, ct);
        var collectorNames = await context.CollectorUsers
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.FullName ?? "", ct);

        // Admin/Head recorders: admin-recorded entries carry no CollectorId — the actor is captured
        // in the audit CreatedBy (username). Map username → full name so the dashboard can attribute them.
        var adminNames = await context.AdminUsers
            .AsNoTracking()
            .Where(a => a.Username != null)
            .ToDictionaryAsync(a => a.Username!, a => a.FullName ?? a.Username!, ct);

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
            p.CreatedBy,
            p.PaidAt!.Value)).ToList();

        recentRows.AddRange((await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.IsPaid)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Take(recentTake)
            .Select(d => new
            {
                d.ORNumber,
                Occupant = d.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                Code = d.Stall.Facility!.Code,
                d.DailyFee,
                d.FishKilos,
                d.CollectorId,
                d.CreatedBy,
                At = d.UpdatedAt ?? d.CreatedAt
            })
            .ToListAsync(ct))
            .Select(d => new RecentRow(d.ORNumber ?? "", d.Occupant ?? "", d.Code,
                d.DailyFee + (d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo, d.CollectorId, d.CreatedBy, At: d.At)));

        // One slaughter receipt (OR) can cover several animal-type line-items, each stored as its
        // own row. Collapse them into a single feed entry showing the receipt's total amount.
        var slhRows = await context.SlaughterTransactions
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(recentTake * 3)
            .Select(s => new { s.ORNumber, s.OwnerName, s.TransactionDate, Amount = s.RatePerHead * s.NumberOfHeads, s.CollectorId, s.CreatedBy, At = s.UpdatedAt ?? s.CreatedAt })
            .ToListAsync(ct);

        recentRows.AddRange(slhRows
            .GroupBy(s => new { s.ORNumber, s.OwnerName, s.TransactionDate })
            .Select(g => new RecentRow(
                g.Key.ORNumber ?? "",
                g.Key.OwnerName,
                FacilityCode.SLH,
                g.Sum(x => x.Amount),
                g.First().CollectorId,
                g.First().CreatedBy,
                g.Max(x => x.At))));

        recentRows.AddRange((await context.TrmTrips
            .AsNoTracking()
            .OrderByDescending(t => t.RecordedAt)
            .Take(recentTake)
            .Select(t => new { t.ORNumber, t.DriverName, t.Fee, t.CollectorId, t.CreatedBy, t.RecordedAt })
            .ToListAsync(ct))
            .Select(t => new RecentRow(t.ORNumber ?? "", t.DriverName, FacilityCode.TRM, t.Fee, t.CollectorId, t.CreatedBy, t.RecordedAt)));

        recentRows.AddRange((await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.IsPaid)
            .OrderByDescending(a => a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt)
            .Take(recentTake)
            .Select(a => new { a.ORNumber, Vendor = a.Vendor!.VendorName, a.Fee, a.CollectorId, a.CreatedBy, At = a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt })
            .ToListAsync(ct))
            .Select(a => new RecentRow(a.ORNumber ?? "", a.Vendor, FacilityCode.TPM, a.Fee, a.CollectorId, a.CreatedBy, a.At)));

        var recent = recentRows
            .OrderByDescending(r => r.At)
            .Take(recentTake)
            .Select(r => new DashboardTransactionDto(
                r.OR,
                r.Payor,
                r.Facility,
                r.Amount,
                ResolveRecorder(r, collectorNames, adminNames),
                r.At))
            .ToList();

        // Delinquents come from the shared rolling-window computation (excludes the current month),
        // so the dashboard and the Financial Reports attention list agree.
        var delinquents = (await facilityReports.GetDelinquentStallsAsync(null, year, month, ct))
            .Take(5)
            .Select(d => new DashboardDelinquentDto(d.Occupant, d.StallNo, d.FacilityCode, d.MonthsUnpaid, d.OutstandingBalance))
            .ToList();

        // Hero collection rate is AMOUNT-based (collected ÷ billed), matching the Financial Reports
        // page and the dashboard's own per-facility cards. Count-based "fully-paid stalls" understated
        // it (e.g. NPM stalls that paid most of their daily fees but aren't a fully-Paid monthly record).
        var totalCollected = facilityCards.Sum(c => c.Collected);
        var billed = totalCollected + totalPending;

        return new DashboardOverviewDto(
            TotalCollected: totalCollected,
            TotalPending: totalPending,
            UnpaidCount: heroUnpaidCount,
            PaidCount: paidCount,
            CollectionRate: billed <= 0m ? 0 : (int)Math.Round(totalCollected / billed * 100m),
            ActiveFacilitiesCount: facilityCards.Count(c => c.Collected > 0),
            TotalCollectors: totalCollectors,
            Facilities: facilityCards,
            RecentTransactions: recent,
            DelinquentVendors: delinquents);
    }

    // Normalized recent-transaction row used to merge the latest collections across all facilities.
    private sealed record RecentRow(
        string OR, string Payor, FacilityCode Facility, decimal Amount, Guid? CollectorId, string? RecordedBy, DateTime At);

    // Who recorded a transaction: the field collector (CollectorId) when set, otherwise the
    // admin/Head captured in the audit CreatedBy. Falls back to the raw actor (e.g. "System").
    private static string ResolveRecorder(
        RecentRow row,
        IReadOnlyDictionary<Guid, string> collectorNames,
        IReadOnlyDictionary<string, string> adminNames)
    {
        if (row.CollectorId is { } id
            && collectorNames.TryGetValue(id, out var collector)
            && !string.IsNullOrWhiteSpace(collector))
            return collector;

        if (!string.IsNullOrWhiteSpace(row.RecordedBy))
            return adminNames.TryGetValue(row.RecordedBy, out var admin) ? admin : row.RecordedBy;

        return "—";
    }
}
