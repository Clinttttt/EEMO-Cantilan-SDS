using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Infrastructure.Repositories;
// Partial of CollectorRepository: what the OFFICE reads about its collectors (ICollectorReportingQueries) — the staff list
// with each collector's period figures, and one collector's activity for a month.
//
// A different question from what the collector's own app asks, and from an account lookup, which is why the contracts are
// separate even though the arithmetic is shared.
public partial class CollectorRepository
{
    public async Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var collectors = await _context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .ToListAsync(cancellationToken);

        var collectorIds = collectors.Select(c => c.Id).ToList();

        // Resolve the municipality's fish rate as of the period (constant fallback → Cantilan unchanged).
        var npmFish = (await _feeRateResolver.GetSnapshotAsync(cancellationToken))
            .Resolve(FeeRateKey.NpmFishPerKilo, new DateOnly(year, month, 1));

        var (monthStartUtc, monthEndUtc) = PhilippineTime.MonthUtcRange(year, month);

        // WHAT EACH COLLECTOR TOOK IN THE MONTH, on the same basis as their Report of Collections.
        //
        // These two counted a rental by the month it was BILLED for and a daily fee by the day the fee was FOR, while the
        // report counted both by the moment the money was recorded. The screens therefore disagreed the moment an owed day
        // or a late rental was settled — ₱566 on the report against ₱536 here, the gap being a single August day collected
        // on 1 September. The office ruled on 2026-09-10 that this figure is CASH, what the collector handled and must
        // remit, and that a period flattered by arrears is disclosed ON the report instead, which now states how much of
        // its total answered for earlier periods.
        var paymentStats = await _context.PaymentRecords
            .Where(p => p.CollectorId != null && collectorIds.Contains(p.CollectorId.Value)
                        && p.Status != PaymentStatus.Unpaid
                        && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= monthStartUtc
                        && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < monthEndUtc)
            .GroupBy(p => p.CollectorId!.Value)
            .Select(g => new
            {
                CollectorId = g.Key,
                // Fee money only, a part payment credited to the fee first and capped there. Electricity and water are
                // banked separately and are no part of a collector's fee accountability — the rule stated once in
                // CollectorFeeMoney, which the report applies; written out here because EF must translate it to SQL.
                Total = g.Sum(p => p.Status == PaymentStatus.Partial
                    ? (p.PartialAmount < p.BaseRentalAmount + (p.FishKilos ?? 0) * npmFish
                        ? p.PartialAmount
                        : p.BaseRentalAmount + (p.FishKilos ?? 0) * npmFish)
                    : p.BaseRentalAmount + (p.FishKilos ?? 0) * npmFish),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        // Paid rows only: an absence carries the day's fee, so counting every row made a stall marked absent look like
        // money in hand. DailyFee already includes any month-end difference carried on an installment; fish remains a
        // separate amount resolved at the tenant's rate.
        var dailyStats = await _context.DailyCollections
            .Where(d => d.CollectorId != null && collectorIds.Contains(d.CollectorId.Value)
                        && d.IsPaid
                        && (d.UpdatedAt ?? d.CreatedAt) >= monthStartUtc
                        && (d.UpdatedAt ?? d.CreatedAt) < monthEndUtc)
            .GroupBy(d => d.CollectorId!.Value)
            .Select(g => new
            {
                CollectorId = g.Key,
                Total = g.Sum(d => d.DailyFee + (d.FishKilos ?? 0) * npmFish),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        // SLH (per-head slaughter), TRM (per-trip), TPM (per-vendor Friday) collections also carry
        // CollectorId — without these, collectors assigned to those facilities show ₱0 / 0 here.
        var slaughterStats = await _context.SlaughterTransactions
            .Where(s => s.CollectorId != null && collectorIds.Contains(s.CollectorId.Value)
                        && s.TransactionDate.Year == year && s.TransactionDate.Month == month)
            .GroupBy(s => s.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(s => s.RatePerHead * s.NumberOfHeads), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var tripStats = await _context.TrmTrips
            .Where(t => t.CollectorId != null && collectorIds.Contains(t.CollectorId.Value)
                        && t.RecordedAt >= monthStartUtc && t.RecordedAt < monthEndUtc)
            .GroupBy(t => t.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(t => t.Fee), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var tpmStats = await _context.TpmAttendances
            .Where(a => a.CollectorId != null && collectorIds.Contains(a.CollectorId.Value) && a.IsPaid
                        && a.MarketDate.Year == year && a.MarketDate.Month == month)
            .GroupBy(a => a.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(a => a.Fee), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var result = new List<CollectorListDto>();

        var lastRecorded = await LatestRecordedAsync(collectorIds, cancellationToken);

        foreach (var collector in collectors)
        {
            paymentStats.TryGetValue(collector.Id, out var payment);
            dailyStats.TryGetValue(collector.Id, out var daily);
            slaughterStats.TryGetValue(collector.Id, out var slaughter);
            tripStats.TryGetValue(collector.Id, out var trip);
            tpmStats.TryGetValue(collector.Id, out var tpm);

            var lastActive = collector.LastActiveAt;
            if (lastRecorded.TryGetValue(collector.Id, out var recorded) && (lastActive is null || recorded > lastActive))
                lastActive = recorded;

            result.Add(new CollectorListDto(
                collector.Id,
                collector.FullName!,
                collector.Email!,
                collector.EmployeeId!,
                collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
                (payment?.Total ?? 0m) + (daily?.Total ?? 0m) + (slaughter?.Total ?? 0m) + (trip?.Total ?? 0m) + (tpm?.Total ?? 0m),
                (payment?.Count ?? 0) + (daily?.Count ?? 0) + (slaughter?.Count ?? 0) + (trip?.Count ?? 0) + (tpm?.Count ?? 0),
                lastActive,
                collector.IsActive));
        }

        return result.OrderByDescending(c => c.LastActiveAt).ToList();
    }

    /// <summary>
    /// When each collector last recorded something, across every source that carries a collector.
    /// </summary>
    /// <remarks>
    /// <see cref="CollectorUser.LastActiveAt"/> is written by one method only — RecordLogin — so on its own it is a last
    /// SIGN-IN stamp under a heading that says activity. The mobile app holds its token, and an entry can be recorded
    /// against a collector without one signing in at all, so a collector who took money today read as days idle: Juan Dels
    /// recorded a daily collection at 23:34 on 16 September while the screen said "Sep 12", his last sign-in.
    ///
    /// <para>
    /// Deliberately not scoped to the month a screen is showing: a collector who last worked in August was last active in
    /// August, not never. And computed on read rather than stamped on every recording path — nothing about collecting money
    /// should depend on remembering to update a column, and this way the figure is right for entries already made.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, DateTime>> LatestRecordedAsync(
        IReadOnlyCollection<Guid> collectorIds, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<Guid, DateTime>();

        void Note(Guid collectorId, DateTime? at)
        {
            if (at is not { } when) return;
            if (!latest.TryGetValue(collectorId, out var held) || when > held) latest[collectorId] = when;
        }

        // Each source is asked when it was last written for these collectors. The timestamp is the one that source's own
        // month figures are counted by, so "last active" and "collected this month" can never disagree about an entry.
        foreach (var row in await _context.PaymentRecords
                     .Where(p => p.CollectorId != null && collectorIds.Contains(p.CollectorId.Value) && p.Status != PaymentStatus.Unpaid)
                     .GroupBy(p => p.CollectorId!.Value)
                     .Select(g => new { CollectorId = g.Key, Latest = g.Max(p => p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) })
                     .ToListAsync(cancellationToken))
            Note(row.CollectorId, row.Latest);

        foreach (var row in await _context.DailyCollections
                     .Where(d => d.CollectorId != null && collectorIds.Contains(d.CollectorId.Value))
                     .GroupBy(d => d.CollectorId!.Value)
                     .Select(g => new { CollectorId = g.Key, Latest = g.Max(d => d.UpdatedAt ?? d.CreatedAt) })
                     .ToListAsync(cancellationToken))
            Note(row.CollectorId, row.Latest);

        foreach (var row in await _context.SlaughterTransactions
                     .Where(s => s.CollectorId != null && collectorIds.Contains(s.CollectorId.Value))
                     .GroupBy(s => s.CollectorId!.Value)
                     .Select(g => new { CollectorId = g.Key, Latest = g.Max(s => s.UpdatedAt ?? s.CreatedAt) })
                     .ToListAsync(cancellationToken))
            Note(row.CollectorId, row.Latest);

        foreach (var row in await _context.TrmTrips
                     .Where(t => t.CollectorId != null && collectorIds.Contains(t.CollectorId.Value))
                     .GroupBy(t => t.CollectorId!.Value)
                     .Select(g => new { CollectorId = g.Key, Latest = g.Max(t => t.RecordedAt) })
                     .ToListAsync(cancellationToken))
            Note(row.CollectorId, row.Latest);

        foreach (var row in await _context.TpmAttendances
                     .Where(a => a.CollectorId != null && collectorIds.Contains(a.CollectorId.Value))
                     .GroupBy(a => a.CollectorId!.Value)
                     .Select(g => new { CollectorId = g.Key, Latest = g.Max(a => a.UpdatedAt ?? a.CreatedAt) })
                     .ToListAsync(cancellationToken))
            Note(row.CollectorId, row.Latest);

        // The market's utility bills carry a collector too, and a collector who only read meters is still working.
        foreach (var row in await _context.UtilityBills
                     .Where(b => b.CollectorId != null && collectorIds.Contains(b.CollectorId.Value))
                     .GroupBy(b => b.CollectorId!.Value)
                     .Select(g => new { CollectorId = g.Key, Latest = g.Max(b => b.UpdatedAt ?? b.CreatedAt) })
                     .ToListAsync(cancellationToken))
            Note(row.CollectorId, row.Latest);

        return latest;
    }

    public async Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default)
    {
        var collector = await _context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == collectorId, cancellationToken);

        if (collector is null)
            return null;

        // Resolve this municipality's fish rate for the period (constant fallback -> Cantilan ₱1/kg).
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(cancellationToken);
        var fishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, new DateOnly(year, month, 1));

        var (mStartUtc, mEndUtc) = PhilippineTime.MonthUtcRange(year, month);

        // WHAT THIS COLLECTOR TOOK IN THE MONTH, on the same basis as their Report of Collections.
        //
        // It used to count a daily fee by the day the fee was FOR and a rental by the month it was BILLED for, while the
        // report counted both by the moment the money was recorded. So the two screens disagreed whenever an owed day or a
        // late rental was settled: ₱566 on the report against ₱536 here, the gap being one August day collected on 1
        // September. The office ruled on 2026-09-10 that this figure is CASH — what the collector handled and must remit —
        // and that a period flattered by arrears is instead disclosed on the report, which now states how much of its total
        // answered for earlier periods.
        //
        // Three other faults went with it. There was NO paid filter, so a row the collector had marked absent would have
        // counted its ₱30 as money (absences do carry a fee); the month-end difference was omitted, so a settled short
        // month understated what was taken; and utilities were included, which the office banks separately and the report
        // excludes. All three now match the report exactly.
        var collectedThisMonth = await _context.DailyCollections
            .Where(d => d.CollectorId == collector.Id
                        && d.IsPaid
                        && (d.UpdatedAt ?? d.CreatedAt) >= mStartUtc
                        && (d.UpdatedAt ?? d.CreatedAt) < mEndUtc)
            .SumAsync(d => d.DailyFee + ((d.FishKilos ?? 0) * fishRate), cancellationToken) +
            await _context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id
                        && p.Status != PaymentStatus.Unpaid
                        && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= mStartUtc
                        && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < mEndUtc)
            // Fee money only, and a part payment credited to the fee first and capped there — the rule stated once in
            // CollectorFeeMoney and applied in the report. Written out here because EF must translate it to SQL.
            .SumAsync(p => p.Status == PaymentStatus.Partial
                          ? (p.PartialAmount < p.BaseRentalAmount + ((p.FishKilos ?? 0) * fishRate)
                              ? p.PartialAmount
                              : p.BaseRentalAmount + ((p.FishKilos ?? 0) * fishRate))
                          : p.BaseRentalAmount + ((p.FishKilos ?? 0) * fishRate), cancellationToken);

        collectedThisMonth +=
            await _context.SlaughterTransactions
                .Where(s => s.CollectorId == collector.Id && s.TransactionDate.Year == year && s.TransactionDate.Month == month)
                .SumAsync(s => s.RatePerHead * s.NumberOfHeads, cancellationToken) +
            await _context.TrmTrips
                .Where(t => t.CollectorId == collector.Id && t.RecordedAt >= mStartUtc && t.RecordedAt < mEndUtc)
                .SumAsync(t => t.Fee, cancellationToken) +
            await _context.TpmAttendances
                .Where(a => a.CollectorId == collector.Id && a.IsPaid && a.MarketDate.Year == year && a.MarketDate.Month == month)
                .SumAsync(a => a.Fee, cancellationToken);

        // Counted over the SAME set the total sums, for the same reason: a count on one basis beside money on another
        // invites the office to divide one by the other and get a figure that means nothing.
        var transactions = await _context.PaymentRecords
            .CountAsync(p => p.CollectorId == collector.Id
                            && p.Status != PaymentStatus.Unpaid
                            && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= mStartUtc
                            && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < mEndUtc, cancellationToken) +
            await _context.DailyCollections
            .CountAsync(d => d.CollectorId == collector.Id
                            && d.IsPaid
                            && (d.UpdatedAt ?? d.CreatedAt) >= mStartUtc
                            && (d.UpdatedAt ?? d.CreatedAt) < mEndUtc, cancellationToken) +
            await _context.SlaughterTransactions
            .CountAsync(s => s.CollectorId == collector.Id && s.TransactionDate.Year == year && s.TransactionDate.Month == month, cancellationToken) +
            await _context.TrmTrips
            .CountAsync(t => t.CollectorId == collector.Id && t.RecordedAt >= mStartUtc && t.RecordedAt < mEndUtc, cancellationToken) +
            await _context.TpmAttendances
            .CountAsync(a => a.CollectorId == collector.Id && a.IsPaid && a.MarketDate.Year == year && a.MarketDate.Month == month, cancellationToken);

        var recentPayments = await _context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id && p.Status != PaymentStatus.Unpaid)
            .OrderByDescending(p => p.PaidAt ?? p.UpdatedAt)
            .Take(10)
            .Select(p => new RecentTransactionDto(
                p.ORNumber ?? "—",
                p.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? "—",
                p.Stall.Facility!.Code,
                "Stall Rental",
                p.Status == PaymentStatus.Paid
                    ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * fishRate)
                    : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m,
                p.Status.ToString(),
                p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);

        // NPM collectors record daily collections (not monthly PaymentRecords), so these must be
        // merged in or the Recent Transactions list would be empty for them.
        var recentDaily = await _context.DailyCollections
            .Where(d => d.CollectorId == collector.Id && d.IsPaid)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Take(10)
            .Select(d => new RecentTransactionDto(
                d.ORNumber ?? "—",
                d.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? "—",
                d.Stall.Facility!.Code,
                "Daily Fee",
                d.DailyFee + ((d.FishKilos ?? 0) * fishRate),
                "Paid",
                d.UpdatedAt ?? d.CreatedAt))
            .ToListAsync(cancellationToken);

        // Per-transaction facilities (SLH/TRM/TPM) — these never produce PaymentRecords or
        // DailyCollections, so their recorded activity must be merged in explicitly.
        var recentSlaughter = await _context.SlaughterTransactions
            .Where(s => s.CollectorId == collector.Id)
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(10)
            .Select(s => new RecentTransactionDto(
                s.ORNumber ?? "—",
                s.OwnerName,
                FacilityCode.SLH,
                "Slaughter",
                s.RatePerHead * s.NumberOfHeads,
                "Paid",
                s.UpdatedAt ?? s.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentTrips = await _context.TrmTrips
            .Where(t => t.CollectorId == collector.Id)
            .OrderByDescending(t => t.RecordedAt)
            .Take(10)
            .Select(t => new RecentTransactionDto(
                t.ORNumber ?? "—",
                t.DriverName,
                FacilityCode.TRM,
                "Terminal Trip",
                t.Fee,
                "Paid",
                t.RecordedAt))
            .ToListAsync(cancellationToken);

        var recentTpm = await _context.TpmAttendances
            .Where(a => a.CollectorId == collector.Id && a.IsPaid)
            .OrderByDescending(a => a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt)
            .Take(10)
            .Select(a => new RecentTransactionDto(
                a.ORNumber ?? "—",
                a.Vendor!.VendorName,
                FacilityCode.TPM,
                "Market Day",
                a.Fee,
                "Paid",
                a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentTransactions = recentPayments
            .Concat(recentDaily)
            .Concat(recentSlaughter)
            .Concat(recentTrips)
            .Concat(recentTpm)
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .ToList();

        var lastRecorded = await LatestRecordedAsync(new[] { collector.Id }, cancellationToken);
        var lastActive = collector.LastActiveAt;
        if (lastRecorded.TryGetValue(collector.Id, out var recorded) && (lastActive is null || recorded > lastActive))
            lastActive = recorded;

        return new CollectorActivityDto(
            collector.Id,
            collector.FullName!,
            collector.EmployeeId!,
            collector.Email!,
            collector.ContactNumber!,
            collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
            collectedThisMonth,
            transactions,
            collector.FacilityAssignments.Count,
            lastActive,
            recentTransactions,
            collector.Username ?? string.Empty);
    }
}
