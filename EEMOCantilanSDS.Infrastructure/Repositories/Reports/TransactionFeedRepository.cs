using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
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
public class TransactionFeedRepository(AppDbContext context, IFeeRateResolver feeRateResolver, IClock clock,
    ISlaughterAnimalLabelProvider? slaughterLabelProvider = null) : ITransactionFeedRepository
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public TransactionFeedRepository(AppDbContext context) : this(context, new FeeRateResolver(context), new SystemClock(), new SlaughterAnimalLabelProvider(context)) { }

    // Resolved NPM fish rate for the in-flight feed build; defaults to the ordinance constant so
    // Cantilan is byte-for-byte, refreshed per call in GetRecentTransactionsAsync.
    private decimal _npmFishRate = FeeRates.NpmFishFeePerKilo;
    private readonly ISlaughterAnimalLabelProvider? _slaughterLabelProvider = slaughterLabelProvider;

    // Tenant facility names, resolved once per feed build. Stall/daily rows read Facility.Name via their
    // navigation; the transaction facilities TRM/TPM don't join Facility, so their display name is looked
    // up here. Falls back to the canonical default, so Cantilan's feed is byte-for-byte unchanged.
    private IReadOnlyDictionary<FacilityCode, string> _facilityNames = new Dictionary<FacilityCode, string>();

    private string TenantFacilityName(FacilityCode code, string fallback) =>
        _facilityNames.TryGetValue(code, out var n) && !string.IsNullOrWhiteSpace(n) ? n : fallback;

    public async Task<IReadOnlyList<TransactionFeedDto>> GetRecentTransactionsAsync(
        FacilityCode? facility, DateOnly? onDate, int limit, CancellationToken ct = default,
        (DateTime StartUtc, DateTime EndUtc)? window = null)
    {
        if (limit <= 0) limit = 100;
        var all = facility is null;
        var results = new List<TransactionFeedDto>();

        // One window for every source, resolved once. A single day IS a window, so onDate is converted here rather than
        // each of the five sources calling DayUtcRange for itself — which is what they used to do, five times over. A
        // caller asking for a period passes its own window; onDate wins if both arrive, because it is the narrower claim.
        //
        // Both forms are carried because the sources are not alike: stall payments, daily collections and terminal trips
        // are stamped with a UTC instant, while slaughterhouse transactions and market attendance carry a Philippine
        // CALENDAR DATE. Converting one to the other at each call site is how an off-by-a-day creeps into a report.
        FeedWindow? effective = onDate is { } day
            ? FeedWindow.ForDay(day)
            : window is { } w ? FeedWindow.ForUtcRange(w.StartUtc, w.EndUtc) : null;

        // Resolve the municipality's fish rate as of the requested date (falls back to the ordinance
        // constant, so Cantilan's feed amounts are unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        _npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, onDate ?? DateOnly.FromDateTime(clock.PhilippineNow));

        // Tenant facility names for TRM/TPM feed rows (whose source tables don't join Facility).
        _facilityNames = await context.Facilities
            .AsNoTracking()
            .ToDictionaryAsync(f => f.Code, f => f.Name, ct);

        // Resolve collector names once (small table) to attribute each row: collector-recorded rows show
        // the collector's name; admin/head-recorded rows (CollectorId null) fall back to the audit actor.
        var collectors = await context.CollectorUsers
            .AsNoTracking()
            .ToDictionaryAsync(
                c => c.Id,
                c => string.IsNullOrWhiteSpace(c.FullName) ? (c.Username ?? "Collector") : c.FullName!,
                ct);

        if (all || facility is FacilityCode.NPM or FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE)
            results.AddRange(await StallPaymentRowsAsync(facility, effective, limit, collectors, ct));

        if (all || facility is FacilityCode.NPM)
            results.AddRange(await DailyCollectionRowsAsync(effective, limit, collectors, ct));

        if (all || facility is FacilityCode.SLH)
            results.AddRange(await SlaughterRowsAsync(effective, limit, collectors, ct));

        if (all || facility is FacilityCode.TRM)
            results.AddRange(await TripRowsAsync(effective, limit, collectors, ct));

        if (all || facility is FacilityCode.TPM)
            results.AddRange(await AttendanceRowsAsync(effective, limit, collectors, ct));

        return results
            .OrderByDescending(r => r.OccurredAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// A period to report over, in both of the forms the sources need.
    /// </summary>
    /// <remarks>
    /// The five sources are not stamped alike: stall payments, daily collections and terminal trips carry a UTC instant,
    /// while slaughterhouse transactions and market attendance carry a Philippine calendar date. Resolving the period once,
    /// into both forms, keeps every source filtering on exactly the boundary the caller asked for — converting between the
    /// two at each call site is how a report comes to include or omit a day at its edge.
    /// </remarks>
    private readonly record struct FeedWindow(DateTime StartUtc, DateTime EndUtc, DateOnly FromDate, DateOnly ToDate)
    {
        /// <summary>One Philippine calendar day, which is what the feed has always meant by a date.</summary>
        public static FeedWindow ForDay(DateOnly day)
        {
            var (startUtc, endUtc) = PhilippineTime.DayUtcRange(day);
            return new FeedWindow(startUtc, endUtc, day, day);
        }

        /// <summary>
        /// An explicit UTC window, as a caller reporting on a month or a year already holds it.
        /// </summary>
        /// <remarks>
        /// The calendar bounds are read back from the window in Philippine time, and the end is taken one tick INSIDE it,
        /// because the window's end is exclusive: a month ending at midnight on the 1st must not admit the 1st.
        /// </remarks>
        public static FeedWindow ForUtcRange(DateTime startUtc, DateTime endUtc) =>
            new(startUtc,
                endUtc,
                DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(startUtc)),
                DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(endUtc.AddTicks(-1))));
    }

    // Attribution: collector-recorded rows resolve to the collector's name; admin/head-recorded rows
    // (CollectorId null) fall back to the audit actor stored in CreatedBy.
    private static string Recorder(Guid? collectorId, string? createdBy, IReadOnlyDictionary<Guid, string> collectors)
    {
        if (collectorId is { } id)
            return collectors.TryGetValue(id, out var name) ? name : "Collector";
        return string.IsNullOrWhiteSpace(createdBy) ? "Admin" : createdBy!;
    }

    private async Task<List<TransactionFeedDto>> StallPaymentRowsAsync(FacilityCode? facility, FeedWindow? win, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.PaymentRecords.AsNoTracking().Where(p => p.Status != PaymentStatus.Unpaid);
        if (facility is not null)
            q = q.Where(p => p.Stall!.Facility!.Code == facility);
        if (win is { } w)
        {
            q = q.Where(p => (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= w.StartUtc
                          && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < w.EndUtc);
        }

        var rows = await q
            .OrderByDescending(p => p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.StallId,
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
                Recorder(r.CollectorId, r.CreatedBy, collectors),
                r.StallId);
        }).ToList();
    }

    private async Task<List<TransactionFeedDto>> DailyCollectionRowsAsync(FeedWindow? win, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.DailyCollections.AsNoTracking().Where(d => d.IsPaid);
        if (win is { } w)
        {
            // "Recorded collections" for a date = collections RECORDED (paid) that day, regardless of which
            // day the fee is for. This surfaces a balance / whole-month settlement recorded today under today
            // (e.g. a closed account paying off old dues), matching the page's "Today's recorded collections".
            q = q.Where(x => (x.UpdatedAt ?? x.CreatedAt) >= w.StartUtc
                          && (x.UpdatedAt ?? x.CreatedAt) < w.EndUtc);
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
                var recordedAt = PhilippineTime.ToPhilippineTime(g.Max(x => x.When));

                // A SINGLE day says which day it is for when that is not the day it was recorded. This page lists what
                // was recorded today whatever day each fee answers for, and a row reading only "Stall 1" was taken as
                // today's fee — so an office comparing it against the collector's own screen, which shows today's fee
                // still owed, saw two screens contradict each other about one stall. Both were right; only this one was
                // mute. A fee collected on its own day reads exactly as before.
                var reference = days > 1
                    ? $"Stall {first.StallNo} · {days} days · {feePeriod}"
                    : minC == DateOnly.FromDateTime(recordedAt)
                        ? $"Stall {first.StallNo}"
                        : $"Stall {first.StallNo} · for {minC.ToString("MMM d")}";

                return new TransactionFeedDto(
                    first.Id, first.Code, first.FacilityName, recordedAt, true,
                    party, reference, "Daily Fee", amount, first.ORNumber, "Paid",
                    Recorder(first.CollectorId, first.CreatedBy, collectors),
                    first.StallId);
            })
            .OrderByDescending(t => t.OccurredAt)
            .ToList();
    }

    private async Task<List<TransactionFeedDto>> SlaughterRowsAsync(FeedWindow? win, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var labels = _slaughterLabelProvider is null ? SlaughterAnimalLabels.Canonical : await _slaughterLabelProvider.GetAsync(ct);
        var q = context.SlaughterTransactions.AsNoTracking();
        if (win is { } w)
            q = q.Where(s => s.TransactionDate >= w.FromDate && s.TransactionDate <= w.ToDate);

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
            // One receipt is one row in the feed. The owner is part of the key because an OR is only shared WITHIN a receipt,
            // and it is keyed by person rather than by exact spelling: the two animal lines of one receipt may have been
            // typed with different capitalisation, which must not split the office's receipt into two.
            .GroupBy(r => new { r.ORNumber, Owner = PersonName.MatchKey(r.OwnerName), r.TransactionDate })
            .Select(g =>
            {
                // One receipt (OR) may cover multiple animal types — summarize them and sum the fees.
                var animals = string.Join(", ", g.Select(x =>
                {
                    var name = string.IsNullOrWhiteSpace(x.CustomAnimalType) ? labels.For(x.AnimalType) : x.CustomAnimalType!;
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

    private async Task<List<TransactionFeedDto>> TripRowsAsync(FeedWindow? win, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.TrmTrips.AsNoTracking();
        if (win is { } w)
            q = q.Where(t => t.RecordedAt >= w.StartUtc && t.RecordedAt < w.EndUtc);

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
            r.Id, FacilityCode.TRM, TenantFacilityName(FacilityCode.TRM, "Transport Terminal"), PhilippineTime.ToPhilippineTime(r.When), true,
            r.DriverName,
            $"{r.PlateNumber} · {r.Route}",
            "Terminal Trip", r.Fee, r.ORNumber, "Paid",
            Recorder(r.CollectorId, r.CreatedBy, collectors))).ToList();
    }

    private async Task<List<TransactionFeedDto>> AttendanceRowsAsync(FeedWindow? win, int limit, IReadOnlyDictionary<Guid, string> collectors, CancellationToken ct)
    {
        var q = context.TpmAttendances.AsNoTracking().Where(a => a.IsPaid);
        if (win is { } w)
            q = q.Where(a => a.MarketDate >= w.FromDate && a.MarketDate <= w.ToDate);

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
            r.Id, FacilityCode.TPM, TenantFacilityName(FacilityCode.TPM, "Tabo-an Public Market"), r.MarketDate.ToDateTime(TimeOnly.MinValue), false,
            r.VendorName,
            r.Goods,
            "Market Day", r.Fee, r.ORNumber, "Paid",
            Recorder(r.CollectorId, r.CreatedBy, collectors))).ToList();
    }
}
