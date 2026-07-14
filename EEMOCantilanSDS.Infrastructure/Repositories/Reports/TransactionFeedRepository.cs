using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// Aggregates recorded money-movements from every facility's transaction table into one
/// chronological feed. Each source is queried server-side (AsNoTracking, projected, top-N),
/// then merged in memory. <c>OccurredAt</c> is normalized to the transaction's actual business
/// moment in Philippine local time so the feed reads in real chronological order regardless of
/// when rows were entered. Computed entity properties (TotalBill, AmountPaid, TotalAmount) are
/// re-derived in memory from stored columns since they are not translatable to SQL.
/// </summary>
public class TransactionFeedRepository(AppDbContext context, IFeeRateResolver feeRateResolver) : ITransactionFeedRepository
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public TransactionFeedRepository(AppDbContext context) : this(context, new FeeRateResolver(context)) { }

    // Resolved NPM fish rate for the in-flight feed build; defaults to the ordinance constant so
    // Cantilan is byte-for-byte, refreshed per call in GetRecentTransactionsAsync.
    private decimal _npmFishRate = FeeRates.NpmFishFeePerKilo;

    public async Task<IReadOnlyList<TransactionFeedDto>> GetRecentTransactionsAsync(
        FacilityCode? facility, DateOnly? onDate, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) limit = 100;
        var all = facility is null;
        var results = new List<TransactionFeedDto>();

        // Resolve the municipality's fish rate as of the requested date (falls back to the ordinance
        // constant, so Cantilan's feed amounts are unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        _npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, onDate ?? DateOnly.FromDateTime(PhilippineTime.Now));

        // Resolve collector names once (small table) to attribute each row: collector-recorded rows show
        // the collector's name; admin/head-recorded rows (CollectorId null) fall back to the audit actor.
        var collectors = await context.CollectorUsers
            .AsNoTracking()
            .ToDictionaryAsync(
                c => c.Id,
                c => string.IsNullOrWhiteSpace(c.FullName) ? (c.Username ?? "Collector") : c.FullName!,
                ct);

        if (all || facility is FacilityCode.NPM or FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE)
            results.AddRange(await StallPaymentRowsAsync(facility, onDate, limit, collectors, ct));

        if (all || facility is FacilityCode.NPM)
            results.AddRange(await DailyCollectionRowsAsync(onDate, limit, collectors, ct));

        if (all || facility is FacilityCode.SLH)
            results.AddRange(await SlaughterRowsAsync(onDate, limit, collectors, ct));

        if (all || facility is FacilityCode.TRM)
            results.AddRange(await TripRowsAsync(onDate, limit, collectors, ct));

        if (all || facility is FacilityCode.TPM)
            results.AddRange(await AttendanceRowsAsync(onDate, limit, collectors, ct));

        return results
            .OrderByDescending(r => r.OccurredAt)
            .Take(limit)
            .ToList();
    }

    // Attribution: collector-recorded rows resolve to the collector's name; admin/head-recorded rows
    // (CollectorId null) fall back to the audit actor stored in CreatedBy.
    private static string Recorder(Guid? collectorId, string? createdBy, IReadOnlyDictionary<Guid, string> collectors)
    {
        if (collectorId is { } id)
            return collectors.TryGetValue(id, out var name) ? name : "Collector";
        return string.IsNullOrWhiteSpace(createdBy) ? "Admin" : createdBy!;
    }

    private async Task<List<TransactionFeedDto>> StallPaymentRowsAsync(FacilityCode? facility, DateOnly? onDate, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.PaymentRecords.AsNoTracking().Where(p => p.Status != PaymentStatus.Unpaid);
        if (facility is not null)
            q = q.Where(p => p.Stall!.Facility!.Code == facility);
        if (onDate is { } d)
        {
            var (startUtc, endUtc) = PhilippineTime.DayUtcRange(d);
            q = q.Where(p => (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc
                          && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc);
        }

        var rows = await q
            .OrderByDescending(p => p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                Code = p.Stall!.Facility!.Code,
                FacilityName = p.Stall.Facility.Name,
                p.Stall.StallNo,
                Occupant = p.Stall.Contracts
                    .OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EffectivityDate)
                    .Select(c => c.ActualOccupant).FirstOrDefault(),
                p.Status,
                p.BaseRentalAmount,
                p.PartialAmount,
                p.ElecAmount,
                p.WaterAmount,
                p.FishKilos,
                p.ORNumber,
                p.BillingYear,
                p.BillingMonth,
                p.CollectorId,
                p.CreatedBy,
                When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var total = r.BaseRentalAmount + (r.ElecAmount ?? 0) + (r.WaterAmount ?? 0)
                        + (r.FishKilos.HasValue ? r.FishKilos.Value * _npmFishRate : 0);
            var amount = r.Status == PaymentStatus.Paid ? total
                       : r.Status == PaymentStatus.Partial ? r.PartialAmount
                       : 0m;
            var period = new DateOnly(r.BillingYear, r.BillingMonth, 1).ToString("MMM yyyy");
            return new TransactionFeedDto(
                r.Id, r.Code, r.FacilityName, PhilippineTime.ToPhilippineTime(r.When), true,
                string.IsNullOrWhiteSpace(r.Occupant) ? "Stall " + r.StallNo : r.Occupant!,
                $"Stall {r.StallNo} · for {period}",
                "Monthly Rent", amount, r.ORNumber,
                r.Status == PaymentStatus.Paid ? "Paid" : "Partial",
                Recorder(r.CollectorId, r.CreatedBy, collectors));
        }).ToList();
    }

    private async Task<List<TransactionFeedDto>> DailyCollectionRowsAsync(DateOnly? onDate, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.DailyCollections.AsNoTracking().Where(d => d.IsPaid);
        if (onDate is { } d)
        {
            // "Recorded collections" for a date = collections RECORDED (paid) that day, regardless of which
            // day the fee is for. This surfaces a balance / whole-month settlement recorded today under today
            // (e.g. a closed account paying off old dues), matching the page's "Today's recorded collections".
            var (startUtc, endUtc) = PhilippineTime.DayUtcRange(d);
            q = q.Where(x => (x.UpdatedAt ?? x.CreatedAt) >= startUtc
                          && (x.UpdatedAt ?? x.CreatedAt) < endUtc);
        }

        var rows = await q
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                d.StallId,
                Code = d.Stall!.Facility!.Code,
                FacilityName = d.Stall.Facility.Name,
                d.Stall.StallNo,
                Occupant = d.Stall.Contracts
                    .OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EffectivityDate)
                    .Select(c => c.ActualOccupant).FirstOrDefault(),
                d.DailyFee,
                d.FishKilos,
                d.ORNumber,
                d.CollectorId,
                d.CreatedBy,
                d.CollectionDate,
                When = d.UpdatedAt ?? d.CreatedAt
            })
            .ToListAsync(ct);

        // A single settlement collapses into ONE feed row, summing its days. When an OR is present we key
        // by it; a blank-OR settlement (a whole-month NPM settle stamps no per-day OR) keys by stall + the
        // FEE month — so all its days group into one row regardless of the exact insert second (avoids a
        // clock-minute split), and separate fee months for the same stall stay as distinct rows.
        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.ORNumber)
                ? $"S:{r.StallId}:{r.CollectionDate:yyyyMM}"
                : $"OR:{r.ORNumber}")
            .Select(g =>
            {
                var first = g.First();
                var amount = g.Sum(x => x.DailyFee + (x.FishKilos.HasValue ? x.FishKilos.Value * _npmFishRate : 0));
                var days = g.Count();
                var party = string.IsNullOrWhiteSpace(first.Occupant) ? "Stall " + first.StallNo : first.Occupant!;

                // Multi-day rows (settlements) show the fee period so a back-dated settle recorded today
                // still reads for the month it is FOR (e.g. "31 days · Jul 2023"), not just "today".
                var minC = g.Min(x => x.CollectionDate);
                var maxC = g.Max(x => x.CollectionDate);
                var feePeriod = minC.Year == maxC.Year && minC.Month == maxC.Month
                    ? minC.ToString("MMM yyyy")
                    : $"{minC.ToString("MMM yyyy")} – {maxC.ToString("MMM yyyy")}";
                var reference = days > 1
                    ? $"Stall {first.StallNo} · {days} days · {feePeriod}"
                    : $"Stall {first.StallNo}";

                return new TransactionFeedDto(
                    first.Id, first.Code, first.FacilityName, PhilippineTime.ToPhilippineTime(g.Max(x => x.When)), true,
                    party, reference, "Daily Fee", amount, first.ORNumber, "Paid",
                    Recorder(first.CollectorId, first.CreatedBy, collectors));
            })
            .OrderByDescending(t => t.OccurredAt)
            .ToList();
    }

    private async Task<List<TransactionFeedDto>> SlaughterRowsAsync(DateOnly? onDate, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.SlaughterTransactions.AsNoTracking();
        if (onDate is { } d)
            q = q.Where(s => s.TransactionDate == d);

        var rows = await q
            .OrderByDescending(s => s.TransactionDate)
            .Take(limit)
            .Select(s => new
            {
                s.Id,
                FacilityName = s.Facility!.Name,
                s.OwnerName,
                s.AnimalType,
                s.CustomAnimalType,
                s.NumberOfHeads,
                s.RatePerHead,
                s.ORNumber,
                s.CollectorId,
                s.CreatedBy,
                s.TransactionDate
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.ORNumber, r.OwnerName, r.TransactionDate })
            .Select(g =>
            {
                // One receipt (OR) may cover multiple animal types — summarize them and sum the fees.
                var animals = string.Join(", ", g.Select(x =>
                {
                    var name = string.IsNullOrWhiteSpace(x.CustomAnimalType) ? x.AnimalType.ToString() : x.CustomAnimalType!;
                    return $"{name} \u00d7{x.NumberOfHeads}";
                }));
                var first = g.First();
                return new TransactionFeedDto(
                    first.Id, FacilityCode.SLH, string.IsNullOrWhiteSpace(first.FacilityName) ? "Slaughterhouse" : first.FacilityName,
                    first.TransactionDate.ToDateTime(TimeOnly.MinValue), false,
                    first.OwnerName,
                    animals,
                    "Slaughter", g.Sum(x => x.RatePerHead * x.NumberOfHeads), first.ORNumber, "Paid",
                    Recorder(first.CollectorId, first.CreatedBy, collectors));
            }).ToList();
    }

    private async Task<List<TransactionFeedDto>> TripRowsAsync(DateOnly? onDate, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.TrmTrips.AsNoTracking();
        if (onDate is { } d)
        {
            var (startUtc, endUtc) = PhilippineTime.DayUtcRange(d);
            q = q.Where(t => t.RecordedAt >= startUtc && t.RecordedAt < endUtc);
        }

        var rows = await q
            .OrderByDescending(t => t.RecordedAt)
            .Take(limit)
            .Select(t => new
            {
                t.Id,
                t.DriverName,
                t.PlateNumber,
                t.Route,
                t.Fee,
                t.ORNumber,
                t.CollectorId,
                t.CreatedBy,
                When = t.RecordedAt
            })
            .ToListAsync(ct);

        return rows.Select(r => new TransactionFeedDto(
            r.Id, FacilityCode.TRM, "Transport Terminal", PhilippineTime.ToPhilippineTime(r.When), true,
            r.DriverName,
            $"{r.PlateNumber} · {r.Route}",
            "Terminal Trip", r.Fee, r.ORNumber, "Paid",
            Recorder(r.CollectorId, r.CreatedBy, collectors))).ToList();
    }

    private async Task<List<TransactionFeedDto>> AttendanceRowsAsync(DateOnly? onDate, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.TpmAttendances.AsNoTracking().Where(a => a.IsPaid);
        if (onDate is { } d)
            q = q.Where(a => a.MarketDate == d);

        var rows = await q
            .OrderByDescending(a => a.MarketDate)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                VendorName = a.Vendor!.VendorName,
                a.Vendor.Goods,
                a.Fee,
                a.ORNumber,
                a.CollectorId,
                a.CreatedBy,
                a.MarketDate
            })
            .ToListAsync(ct);

        return rows.Select(r => new TransactionFeedDto(
            r.Id, FacilityCode.TPM, "Tabo-an Public Market", r.MarketDate.ToDateTime(TimeOnly.MinValue), false,
            r.VendorName,
            r.Goods,
            "Market Day", r.Fee, r.ORNumber, "Paid",
            Recorder(r.CollectorId, r.CreatedBy, collectors))).ToList();
    }
}
