using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// The unified Collection Activity (IA-011 / IA-050 / ADR-005 §7). Two halves, never concatenated blindly:
/// <list type="bullet">
/// <item>Legacy: the same sources and money as the official Monthly Income's legacy reader, each row listed only while
/// <see cref="CollectionSourceAuthorityMap.LegacyMoneyCounts"/> says its legacy money is authoritative. A rent row or a
/// utility part that has gone canonical is skipped, because its Collection is the record of that money.</item>
/// <item>Canonical: one event per posted Collection in the period, with only the lines whose money
/// <see cref="CollectionSourceAuthorityMap.CanonicalMoneyCounts"/> says is authoritative, and the corrections linked to it.</item>
/// </list>
/// Every query is explicitly tenant-scoped as well as filtered by the context. Nothing is written.
/// </summary>
public sealed class CollectionActivityReader(AppDbContext context, ISlaughterAnimalLabelProvider? slaughterLabels = null)
    : ICollectionActivityReader
{
    private const string Legacy = "Legacy";
    private const string Canonical = "Canonical";

    public async Task<IReadOnlyList<CollectionActivityEventDto>> GetAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (startUtc, _) = PhilippineTime.DayUtcRange(from);
        var (_, endUtc) = PhilippineTime.DayUtcRange(to);
        var window = new Window(tenantId, from, to, startUtc, endUtc);

        var collectors = await context.CollectorUsers.AsNoTracking()
            .Where(c => c.MunicipalityId == tenantId)
            .Select(c => new { c.Id, c.FullName, c.Username })
            .ToDictionaryAsync(c => c.Id, c => string.IsNullOrWhiteSpace(c.FullName) ? c.Username ?? "Collector" : c.FullName!, ct);
        var names = await ClassificationNamesAsync(tenantId, to, ct);

        var events = new List<CollectionActivityEventDto>();
        events.AddRange(await RentAsync(window, collectors, names, ct));
        events.AddRange(await DailyAsync(window, collectors, names, ct));
        events.AddRange(await UtilityAsync(window, collectors, names, ct));
        events.AddRange(await SlaughterAsync(window, collectors, names, ct));
        events.AddRange(await TripsAsync(window, collectors, names, ct));
        events.AddRange(await TaboAsync(window, collectors, names, ct));
        events.AddRange(await CanonicalAsync(window, collectors, ct));
        return events;
    }

    public async Task<DateOnly?> FindBusinessDateByReferenceAsync(Guid tenantId, string referenceCode, CancellationToken ct = default)
    {
        var code = referenceCode.Trim().ToUpperInvariant();
        var date = await context.Collections.AsNoTracking()
            .Where(c => c.MunicipalityId == tenantId && c.ReferenceCode == code)
            .Select(c => (DateOnly?)c.BusinessDate).FirstOrDefaultAsync(ct);
        return date;
    }

    private readonly record struct Window(Guid TenantId, DateOnly From, DateOnly To, DateTime StartUtc, DateTime EndUtc);

    private static DateOnly PhDate(DateTime utc) => DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(utc));

    private static string Recorder(Guid? collectorId, string? createdBy, IReadOnlyDictionary<Guid, string> collectors) =>
        collectorId is { } id
            ? collectors.GetValueOrDefault(id, "Collector")
            : string.IsNullOrWhiteSpace(createdBy) ? "Office" : createdBy!;

    private static string? CollectorName(Guid? collectorId, IReadOnlyDictionary<Guid, string> collectors) =>
        collectorId is { } id ? collectors.GetValueOrDefault(id, "Collector") : null;

    /// <summary>The tenant's default-context display name per semantic code effective by the period end; the code otherwise.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ClassificationNamesAsync(Guid tenantId, DateOnly asOf, CancellationToken ct)
    {
        var rows = await (
            from policy in context.RevenueClassificationPolicies.AsNoTracking()
            join classification in context.RevenueClassifications.AsNoTracking()
                on new { policy.MunicipalityId, Id = policy.RevenueClassificationId } equals new { classification.MunicipalityId, classification.Id }
            where policy.MunicipalityId == tenantId && policy.BusinessContext == RevenuePolicyContext.Default
                && policy.EffectiveDate <= asOf
            select new { classification.SemanticCode, policy.EffectiveDate, policy.DisplayName })
            .ToListAsync(ct);
        return rows.GroupBy(x => x.SemanticCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EffectiveDate).First().DisplayName);
    }

    private static CollectionActivityLineDto LegacyLine(IReadOnlyDictionary<string, string> names, string code, decimal amount,
        CollectionSourceKind kind, CollectionSourcePart? part, Guid sourceId, int? year, int? month, string? description) =>
        new(code, names.GetValueOrDefault(code, code), amount, kind.ToString(), part?.ToString(), sourceId, year, month, description);

    private static CollectionActivityEventDto LegacyEvent(string key, CollectionSourceKind kind, DateOnly date, DateTime? recordedAtUtc,
        string? document, string? payer, Guid? collectorId, string? createdBy, IReadOnlyDictionary<Guid, string> collectors,
        FacilityCode? facility, string? subject, string? status, IReadOnlyList<CollectionActivityLineDto> lines)
    {
        var amount = lines.Sum(l => l.Amount);
        return new CollectionActivityEventDto(key, Legacy, kind.ToString(), null, date, recordedAtUtc,
            null, string.IsNullOrWhiteSpace(document) ? null : document.Trim(), null, [], [], payer, null,
            collectorId, CollectorName(collectorId, collectors), Recorder(collectorId, createdBy, collectors),
            facility, subject, status, amount, 0m, amount, "Recorded", null, lines, []);
    }

    // ── Legacy monthly rent: authoritative only before the row's cutover ──
    private async Task<List<CollectionActivityEventDto>> RentAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        // No frozen price exists on legacy monthly kilogram rows. Current rates cannot price historical evidence.
        const decimal fishRate = 0m;
        var rows = await context.PaymentRecords.AsNoTracking()
            .Where(p => p.MunicipalityId == w.TenantId && p.Status != PaymentStatus.Unpaid
                && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= w.StartUtc && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < w.EndUtc)
            .Select(p => new
            {
                p.Id, p.Status, p.BaseRentalAmount, p.PartialAmount, p.FishKilos, p.ORNumber, p.BillingYear, p.BillingMonth,
                p.SettlementAuthorityState, p.CollectorId, p.CreatedBy, p.Stall!.StallNo, Code = p.Stall.Facility!.Code,
                Occupant = p.Stall.Contracts.OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EffectivityDate)
                    .Select(c => c.ActualOccupant).FirstOrDefault(),
                When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
            }).ToListAsync(ct);

        var events = new List<CollectionActivityEventDto>();
        foreach (var p in rows)
        {
            if (!CollectionSourceAuthorityMap.LegacyMoneyCounts(CollectionSourceKind.PaymentRecord, p.SettlementAuthorityState))
                continue;
            var portion = CollectorFeeMoney.MonthlyFeePortion(p.Status, p.BaseRentalAmount, p.FishKilos, p.PartialAmount, fishRate);
            var rent = Math.Min(portion, p.BaseRentalAmount);
            var code = p.Code == FacilityCode.ICE ? RevenueClassificationCodes.IcePlant : RevenueClassificationCodes.PermanentStallRent;
            var lines = new List<CollectionActivityLineDto>
            {
                LegacyLine(names, code, rent, CollectionSourceKind.PaymentRecord, null, p.Id, p.BillingYear, p.BillingMonth, $"Stall {p.StallNo}")
            };
            if (portion - rent > 0m)
                lines.Add(LegacyLine(names, RevenueClassificationCodes.WeightAndMeasure, portion - rent, CollectionSourceKind.PaymentRecord,
                    null, p.Id, p.BillingYear, p.BillingMonth, "Weighing"));
            if (lines.Sum(l => l.Amount) == 0m) continue;
            events.Add(LegacyEvent($"PaymentRecord:{p.Id}", CollectionSourceKind.PaymentRecord, PhDate(p.When), p.When, p.ORNumber,
                p.Occupant, p.CollectorId, p.CreatedBy, collectors, p.Code,
                $"Stall {p.StallNo} · for {new DateOnly(p.BillingYear, p.BillingMonth, 1):MMM yyyy}",
                p.Status.ToString(), lines));
        }
        return events;
    }

    // ── Legacy NPM daily fees (legacy-only source): one event per receipt, or per stall and fee month without one ──
    private async Task<List<CollectionActivityEventDto>> DailyAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        var rows = await context.DailyCollections.AsNoTracking()
            .Where(d => d.MunicipalityId == w.TenantId
                && (d.IsPaid || (d.SettlementAuthorityState == SettlementAuthority.Canonical && (d.FishKilos > 0m || d.MeatFeeAmount > 0m)))
                && (d.UpdatedAt ?? d.CreatedAt) >= w.StartUtc && (d.UpdatedAt ?? d.CreatedAt) < w.EndUtc)
            .Select(d => new
            {
                d.Id, d.StallId, d.FishKilos, d.FishFeeAmountFrozen, d.MeatFeeAmount, d.ORNumber, d.CollectorId,
                // A day paid by a canonical Collection is listed by that Collection; only its weighing is legacy money here.
                DailyFee = d.SettlementAuthorityState == SettlementAuthority.Canonical ? 0m : d.DailyFee - (d.CanonicalAdjustmentCollectionId != null ? d.MonthEndAdjustment ?? 0m : 0m),
                d.CreatedBy, d.CollectionDate, d.Stall!.StallNo, Code = d.Stall.Facility!.Code,
                Occupant = d.Stall.Contracts.OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EffectivityDate)
                    .Select(c => c.ActualOccupant).FirstOrDefault(),
                When = d.UpdatedAt ?? d.CreatedAt
            }).ToListAsync(ct);

        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.ORNumber) ? $"S:{r.StallId}:{r.CollectionDate:yyyyMM}" : $"OR:{r.ORNumber!.Trim()}")
            .Select(g =>
            {
                var first = g.OrderBy(x => x.Id).First();
                var when = g.Max(x => x.When);
                var min = g.Min(x => x.CollectionDate);
                var max = g.Max(x => x.CollectionDate);
                var days = g.Count(x => x.DailyFee != 0m);
                var fee = g.Sum(x => x.DailyFee);
                var weighing = g.Sum(x => (x.FishFeeAmountFrozen ?? 0m) + x.MeatFeeAmount);
                var period = min == max ? $"{min:MMM d, yyyy}" : $"{min:MMM d} – {max:MMM d, yyyy}";
                var lines = new List<CollectionActivityLineDto>();
                if (fee != 0m)
                    lines.Add(LegacyLine(names, RevenueClassificationCodes.PermanentStallRent, fee, CollectionSourceKind.DailyCollection,
                        CollectionSourcePart.DailyFee, first.Id, min.Year, min.Month, $"Daily fee · {days} day{(days == 1 ? "" : "s")} · {period}"));
                if (weighing != 0m)
                    lines.Add(LegacyLine(names, RevenueClassificationCodes.WeightAndMeasure, weighing, CollectionSourceKind.DailyCollection,
                        null, first.Id, min.Year, min.Month, "Weighing"));
                return LegacyEvent($"DailyCollection:{g.Key}", CollectionSourceKind.DailyCollection, PhDate(when), when,
                    first.ORNumber, first.Occupant, first.CollectorId, first.CreatedBy, collectors, first.Code,
                    $"Stall {first.StallNo}", "Paid", lines);
            })
            .Where(e => e.Amount != 0m)
            .ToList();
    }

    // ── Legacy electricity / water: each part authoritative only before its own cutover ──
    private async Task<List<CollectionActivityEventDto>> UtilityAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        var rows = await context.UtilityBills.AsNoTracking()
            .Where(b => b.MunicipalityId == w.TenantId
                && ((b.ElecStatus != PaymentStatus.Unpaid
                        && (b.ElecPaidAt ?? b.UpdatedAt ?? b.CreatedAt) >= w.StartUtc && (b.ElecPaidAt ?? b.UpdatedAt ?? b.CreatedAt) < w.EndUtc)
                    || (b.WaterStatus != PaymentStatus.Unpaid
                        && (b.WaterPaidAt ?? b.UpdatedAt ?? b.CreatedAt) >= w.StartUtc && (b.WaterPaidAt ?? b.UpdatedAt ?? b.CreatedAt) < w.EndUtc)))
            .Select(b => new
            {
                b.Id, b.BillingYear, b.BillingMonth, b.CollectorId, b.CreatedBy,
                b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount, b.ElecPaidAt, b.ElecORNumber,
                b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount, b.WaterPaidAt, b.WaterORNumber,
                b.ElectricitySettlementAuthorityState, b.WaterSettlementAuthorityState,
                b.Stall!.StallNo, Code = b.Stall.Facility!.Code,
                Occupant = b.Stall.Contracts.OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EffectivityDate)
                    .Select(c => c.ActualOccupant).FirstOrDefault(),
                When = b.UpdatedAt ?? b.CreatedAt
            }).ToListAsync(ct);

        var events = new List<CollectionActivityEventDto>();
        foreach (var b in rows)
        {
            var period = new DateOnly(b.BillingYear, b.BillingMonth, 1);
            void Part(CollectionSourcePart part, SettlementAuthority authority, PaymentStatus status, decimal charge, decimal partial,
                DateTime? paidAt, string? or, string code, string label)
            {
                if (!CollectionSourceAuthorityMap.LegacyMoneyCounts(CollectionSourceKind.UtilityBill, authority)) return;
                var paid = status switch { PaymentStatus.Paid => charge, PaymentStatus.Partial => partial, _ => 0m };
                var at = paidAt ?? b.When;
                if (paid <= 0m || at < w.StartUtc || at >= w.EndUtc) return;
                events.Add(LegacyEvent($"UtilityBill:{b.Id}:{part}", CollectionSourceKind.UtilityBill, PhDate(at), at, or, b.Occupant,
                    b.CollectorId, b.CreatedBy, collectors, b.Code, $"Stall {b.StallNo} · {label} · {period:MMM yyyy}", status.ToString(),
                    [LegacyLine(names, code, paid, CollectionSourceKind.UtilityBill, part, b.Id, b.BillingYear, b.BillingMonth, label)]));
            }
            Part(CollectionSourcePart.Electricity, b.ElectricitySettlementAuthorityState, b.ElecStatus,
                Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh, b.ElecPartialAmount, b.ElecPaidAt,
                b.ElecORNumber, RevenueClassificationCodes.Ecf, "Electricity");
            Part(CollectionSourcePart.Water, b.WaterSettlementAuthorityState, b.WaterStatus,
                Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter, b.WaterPartialAmount, b.WaterPaidAt,
                b.WaterORNumber, RevenueClassificationCodes.Wcf, "Water");
        }
        return events;
    }

    // ── Slaughterhouse (legacy-only): one event per receipt, one line per animal ──
    private async Task<List<CollectionActivityEventDto>> SlaughterAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        var labels = slaughterLabels is null ? SlaughterAnimalLabels.Canonical : await slaughterLabels.GetAsync(ct);
        var rows = await context.SlaughterTransactions.AsNoTracking()
            .Where(s => s.MunicipalityId == w.TenantId && s.TransactionDate >= w.From && s.TransactionDate <= w.To)
            .Select(s => new
            {
                s.Id, s.OwnerName, s.AnimalType, s.CustomAnimalType, s.NumberOfHeads, s.RatePerHead, s.ORNumber, s.CollectorId,
                s.CreatedBy, s.TransactionDate, When = s.UpdatedAt ?? s.CreatedAt
            }).ToListAsync(ct);

        return rows
            .GroupBy(r => new { OR = r.ORNumber?.Trim(), Owner = PersonName.MatchKey(r.OwnerName), r.TransactionDate })
            .Select(g =>
            {
                var first = g.OrderBy(x => x.Id).First();
                var lines = g.OrderBy(x => x.Id).Select(x => LegacyLine(names, RevenueClassificationCodes.Slaughterhouse,
                    x.RatePerHead * x.NumberOfHeads, CollectionSourceKind.SlaughterTransaction, null, x.Id, null, null,
                    $"{(string.IsNullOrWhiteSpace(x.CustomAnimalType) ? labels.For(x.AnimalType) : x.CustomAnimalType)} × {x.NumberOfHeads}"))
                    .ToList();
                return LegacyEvent($"SlaughterTransaction:{first.Id}", CollectionSourceKind.SlaughterTransaction, first.TransactionDate,
                    g.Max(x => x.When), first.ORNumber, first.OwnerName, first.CollectorId, first.CreatedBy, collectors,
                    FacilityCode.SLH, null, "Paid", lines);
            })
            .Where(e => e.Amount != 0m)
            .ToList();
    }

    // ── Terminal trips (legacy-only) ──
    private async Task<List<CollectionActivityEventDto>> TripsAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        var rows = await context.TrmTrips.AsNoTracking()
            .Where(t => t.MunicipalityId == w.TenantId && t.RecordedAt >= w.StartUtc && t.RecordedAt < w.EndUtc)
            .Select(t => new { t.Id, t.DriverName, t.PlateNumber, t.Route, t.Fee, t.ORNumber, t.CollectorId, t.CreatedBy, t.RecordedAt })
            .ToListAsync(ct);
        return rows.Where(t => t.Fee != 0m).Select(t => LegacyEvent($"TrmTrip:{t.Id}", CollectionSourceKind.TrmTrip, PhDate(t.RecordedAt),
            t.RecordedAt, t.ORNumber, t.DriverName, t.CollectorId, t.CreatedBy, collectors, FacilityCode.TRM,
            $"{t.PlateNumber} · {t.Route}", "Paid",
            [LegacyLine(names, RevenueClassificationCodes.TransportationParking, t.Fee, CollectionSourceKind.TrmTrip, null, t.Id, null, null, "Trip fee")]))
            .ToList();
    }

    // ── Tabo-an market days (legacy-only) ──
    private async Task<List<CollectionActivityEventDto>> TaboAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        IReadOnlyDictionary<string, string> names, CancellationToken ct)
    {
        var rows = await context.TpmAttendances.AsNoTracking()
            .Where(a => a.MunicipalityId == w.TenantId && a.IsPaid && a.MarketDate >= w.From && a.MarketDate <= w.To)
            .Select(a => new { a.Id, a.Vendor!.VendorName, a.Vendor.Goods, a.Fee, a.ORNumber, a.CollectorId, a.CreatedBy, a.MarketDate, When = a.UpdatedAt ?? a.CreatedAt })
            .ToListAsync(ct);
        return rows.Where(a => a.Fee != 0m).Select(a => LegacyEvent($"TpmAttendance:{a.Id}", CollectionSourceKind.TpmAttendance, a.MarketDate,
            a.When, a.ORNumber, a.VendorName, a.CollectorId, a.CreatedBy, collectors, FacilityCode.TPM, a.Goods, "Paid",
            [LegacyLine(names, RevenueClassificationCodes.Tabo, a.Fee, CollectionSourceKind.TpmAttendance, null, a.Id, null, null, "Market day")]))
            .ToList();
    }

    // ── Canonical: one event per posted Collection, authoritative lines only, with linked corrections ──
    private async Task<List<CollectionActivityEventDto>> CanonicalAsync(Window w, IReadOnlyDictionary<Guid, string> collectors,
        CancellationToken ct)
    {
        var tenant = w.TenantId;
        var collections = await context.Collections.AsNoTracking()
            .Where(c => c.MunicipalityId == tenant && c.BusinessDate >= w.From && c.BusinessDate <= w.To)
            .Select(c => new { c.Id, c.BusinessDate, c.RecordedAtUtc, c.ReferenceCode, c.ActorName, c.CollectorId, c.PayorId, c.PayerName })
            .ToListAsync(ct);
        if (collections.Count == 0) return [];
        var ids = collections.Select(c => c.Id).ToArray();

        var lines = await context.CollectionLines.AsNoTracking()
            .Where(l => l.MunicipalityId == tenant && ids.Contains(l.CollectionId))
            .Select(l => new { l.Id, l.CollectionId, l.RevenueClassificationId, l.RevenueClassificationPolicyId, l.Amount, l.SourceKind, l.SourceId, l.SourcePart })
            .ToListAsync(ct);
        var lineIds = lines.Select(l => l.Id).ToArray();
        var allocations = await context.CollectionAllocations.AsNoTracking()
            .Where(a => a.MunicipalityId == tenant && lineIds.Contains(a.CollectionLineId))
            .Select(a => new { a.CollectionLineId, a.SourceKind, a.SourceId, a.SourcePart })
            .ToListAsync(ct);

        // Settlement authority and context of the source rows these lines settle.
        var recordIds = lines.Where(l => l.SourceKind == CollectionSourceKind.PaymentRecord && l.SourceId.HasValue).Select(l => l.SourceId!.Value)
            .Concat(allocations.Where(a => a.SourceKind == CollectionSourceKind.PaymentRecord).Select(a => a.SourceId)).Distinct().ToArray();
        var billIds = lines.Where(l => l.SourceKind == CollectionSourceKind.UtilityBill && l.SourceId.HasValue).Select(l => l.SourceId!.Value)
            .Concat(allocations.Where(a => a.SourceKind == CollectionSourceKind.UtilityBill).Select(a => a.SourceId)).Distinct().ToArray();
        var records = await context.PaymentRecords.AsNoTracking()
            .Where(p => p.MunicipalityId == tenant && recordIds.Contains(p.Id))
            .Select(p => new { p.Id, p.SettlementAuthorityState, p.BillingYear, p.BillingMonth, p.Stall!.StallNo, Code = p.Stall.Facility!.Code })
            .ToDictionaryAsync(p => p.Id, ct);
        var bills = await context.UtilityBills.AsNoTracking()
            .Where(b => b.MunicipalityId == tenant && billIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ElectricitySettlementAuthorityState, b.WaterSettlementAuthorityState, b.BillingYear, b.BillingMonth, b.Stall!.StallNo, Code = b.Stall.Facility!.Code })
            .ToDictionaryAsync(b => b.Id, ct);
        // NPM day rows a canonical Collection paid: only for the stall and facility of the line, never for its amount.
        var dayIds = allocations.Where(a => a.SourceKind == CollectionSourceKind.DailyCollection).Select(a => a.SourceId).Distinct().ToArray();
        var days = await context.DailyCollections.AsNoTracking()
            .Where(d => d.MunicipalityId == tenant && dayIds.Contains(d.Id))
            .Select(d => new { d.Id, d.CollectionDate, d.Stall!.StallNo, Code = d.Stall.Facility!.Code })
            .ToDictionaryAsync(d => d.Id, ct);

        bool SourceCounts(CollectionSourceKind? kind, Guid? sourceId, CollectionSourcePart? part)
        {
            if (kind is not { } k) return true;
            // A posted line whose source row cannot be read is still posted money; it is not hidden.
            var authority = k switch
            {
                CollectionSourceKind.PaymentRecord => sourceId is { } r && records.TryGetValue(r, out var rec)
                    ? rec.SettlementAuthorityState : SettlementAuthority.Canonical,
                CollectionSourceKind.UtilityBill => sourceId is { } b && bills.TryGetValue(b, out var bill)
                    ? part == CollectionSourcePart.Water ? bill.WaterSettlementAuthorityState : bill.ElectricitySettlementAuthorityState
                    : SettlementAuthority.Canonical,
                _ => SettlementAuthority.Canonical
            };
            return CollectionSourceAuthorityMap.CanonicalMoneyCounts(k, authority);
        }
        var allocationsByLine = allocations.ToLookup(a => a.CollectionLineId);
        var authoritative = lines
            .Where(l => SourceCounts(l.SourceKind, l.SourceId, l.SourcePart)
                && allocationsByLine[l.Id].All(a => SourceCounts(a.SourceKind, a.SourceId, a.SourcePart)))
            .ToList();
        var authoritativeIds = authoritative.Select(l => l.Id).ToHashSet();

        var classificationIds = authoritative.Select(l => l.RevenueClassificationId).Distinct().ToArray();
        var codes = await context.RevenueClassifications.AsNoTracking()
            .Where(c => c.MunicipalityId == tenant && classificationIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.SemanticCode, ct);
        var policyIds = authoritative.Select(l => l.RevenueClassificationPolicyId).Distinct().ToArray();
        var policyNames = await context.RevenueClassificationPolicies.AsNoTracking()
            .Where(p => p.MunicipalityId == tenant && policyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var policyInstruments = await context.RevenueClassificationPolicies.AsNoTracking()
            .Where(p => p.MunicipalityId == tenant && policyIds.Contains(p.Id) && p.PermittedInstrumentType != null)
            .ToDictionaryAsync(p => p.Id, p => (RevenueInstrumentType?)p.PermittedInstrumentType, ct);

        var corrections = await context.CollectionCorrections.AsNoTracking()
            .Where(c => c.MunicipalityId == tenant && ids.Contains(c.OriginalCollectionId))
            .Select(c => new { c.Id, c.OriginalCollectionId, c.CorrectionType, c.CorrectionEffectiveDate, c.RecordedAtUtc, c.ReplacementCollectionId, c.ReplacementDocumentId, c.Reason })
            .ToListAsync(ct);
        var correctionIds = corrections.Select(c => c.Id).ToArray();
        var correctionLines = await context.CollectionCorrectionLines.AsNoTracking()
            .Where(c => c.MunicipalityId == tenant && correctionIds.Contains(c.CorrectionId))
            .Select(c => new { c.CorrectionId, c.OriginalCollectionLineId, c.FinancialEffectAmount })
            .ToListAsync(ct);
        var replaces = await context.CollectionCorrections.AsNoTracking()
            .Where(c => c.MunicipalityId == tenant && c.ReplacementCollectionId.HasValue && ids.Contains(c.ReplacementCollectionId.Value))
            .Select(c => new { Replacement = c.ReplacementCollectionId!.Value, c.OriginalCollectionId })
            .ToListAsync(ct);

        var replacementDocIds = corrections.Where(c => c.ReplacementDocumentId.HasValue).Select(c => c.ReplacementDocumentId!.Value).ToArray();
        var documents = await context.AccountableDocuments.AsNoTracking()
            .Where(d => d.MunicipalityId == tenant
                && ((d.CollectionId.HasValue && ids.Contains(d.CollectionId.Value)) || replacementDocIds.Contains(d.Id)))
            .Select(d => new { d.Id, d.CollectionId, d.DocumentNumber, d.InstrumentType })
            .ToListAsync(ct);
        var replacementDocSet = replacementDocIds.ToHashSet();
        var replacementCollectionIds = corrections.Where(c => c.ReplacementCollectionId.HasValue)
            .Select(c => c.ReplacementCollectionId!.Value).Distinct().ToArray();
        var replacementCodes = replacementCollectionIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Collections.AsNoTracking()
                .Where(x => x.MunicipalityId == tenant && replacementCollectionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.ReferenceCode, ct);

        var linesByCollection = authoritative.ToLookup(l => l.CollectionId);
        var events = new List<CollectionActivityEventDto>();
        foreach (var c in collections)
        {
            var own = linesByCollection[c.Id].OrderBy(l => l.Id).ToList();
            // Every line of this Collection settles a row whose legacy money is authoritative: it is shadow evidence, and the
            // legacy row above is the one record of that money.
            if (own.Count == 0) continue;

            var detail = own.Select(l =>
            {
                var code = codes.GetValueOrDefault(l.RevenueClassificationId, "UNKNOWN_CLASSIFICATION");
                var sourceId = l.SourceId ?? allocationsByLine[l.Id].Select(a => (Guid?)a.SourceId).FirstOrDefault();
                var kind = l.SourceKind ?? allocationsByLine[l.Id].Select(a => (CollectionSourceKind?)a.SourceKind).FirstOrDefault();
                int? year = null, month = null;
                string? description = null;
                if (kind == CollectionSourceKind.PaymentRecord && sourceId is { } r && records.TryGetValue(r, out var rec))
                    (year, month, description) = (rec.BillingYear, rec.BillingMonth, $"Stall {rec.StallNo}");
                else if (kind == CollectionSourceKind.UtilityBill && sourceId is { } b && bills.TryGetValue(b, out var bill))
                    (year, month, description) = (bill.BillingYear, bill.BillingMonth, $"Stall {bill.StallNo}");
                else if (kind == CollectionSourceKind.DailyCollection && sourceId is { } dayId && days.TryGetValue(dayId, out var day))
                    (year, month, description) = (day.CollectionDate.Year, day.CollectionDate.Month, $"Stall {day.StallNo}");
                return new CollectionActivityLineDto(code, policyNames.GetValueOrDefault(l.RevenueClassificationPolicyId, code),
                    l.Amount, kind?.ToString(), l.SourcePart?.ToString(), sourceId, year, month, description);
            }).ToList();

            var facilities = own.SelectMany(l => new[] { (l.SourceKind, l.SourceId) }
                    .Concat(allocationsByLine[l.Id].Select(a => ((CollectionSourceKind?)a.SourceKind, (Guid?)a.SourceId))))
                .Select(s => s.Item1 == CollectionSourceKind.PaymentRecord && s.Item2 is { } r && records.TryGetValue(r, out var rec) ? (FacilityCode?)rec.Code
                    : s.Item1 == CollectionSourceKind.UtilityBill && s.Item2 is { } b && bills.TryGetValue(b, out var bill) ? bill.Code
                    : s.Item1 == CollectionSourceKind.DailyCollection && s.Item2 is { } d && days.TryGetValue(d, out var day) ? day.Code
                    : null)
                .Where(f => f.HasValue).Distinct().ToList();
            var stalls = detail.Select(d => d.Description).Where(d => d is not null).Distinct().ToList();

            var linked = corrections.Where(x => x.OriginalCollectionId == c.Id).OrderBy(x => x.RecordedAtUtc).ToList();
            var correctionDtos = linked.Select(x => new CollectionActivityCorrectionDto(x.Id, x.CorrectionType.ToString(),
                x.CorrectionEffectiveDate, x.RecordedAtUtc,
                correctionLines.Where(l => l.CorrectionId == x.Id && authoritativeIds.Contains(l.OriginalCollectionLineId)).Sum(l => l.FinancialEffectAmount),
                x.ReplacementCollectionId, x.Reason)).ToList();
            var amount = own.Sum(l => l.Amount);
            var effect = correctionDtos.Sum(x => x.FinancialEffect);
            var disposition = linked.Count == 0 ? "Posted"
                : linked.Any(x => x.CorrectionType == CollectionCorrectionType.Void) ? "Voided"
                : linked.Any(x => x.CorrectionType == CollectionCorrectionType.Reversal) ? "Reversed"
                : linked.Any(x => x.CorrectionType == CollectionCorrectionType.Replacement) ? "Replaced"
                : "Document corrected";

            var linkedReplacementDocs = linked.Where(x => x.ReplacementDocumentId.HasValue).Select(x => x.ReplacementDocumentId!.Value).ToHashSet();
            var primary = documents.Where(d => d.CollectionId == c.Id && !replacementDocSet.Contains(d.Id)).OrderBy(d => d.DocumentNumber).FirstOrDefault()
                ?? documents.Where(d => d.CollectionId == c.Id).OrderBy(d => d.DocumentNumber).FirstOrDefault();
            var replacementNumbers = documents.Where(d => linkedReplacementDocs.Contains(d.Id) && d.Id != primary?.Id)
                .Select(d => d.DocumentNumber).OrderBy(n => n, StringComparer.Ordinal).ToList();

            events.Add(new CollectionActivityEventDto($"Collection:{c.Id}", Canonical, "Collection", c.Id, c.BusinessDate,
                c.RecordedAtUtc, c.ReferenceCode, primary?.DocumentNumber, primary?.InstrumentType ?? policyInstruments.GetValueOrDefault(own[0].RevenueClassificationPolicyId), replacementNumbers,
                linked.Where(x => x.ReplacementCollectionId.HasValue).Select(x => replacementCodes.GetValueOrDefault(x.ReplacementCollectionId!.Value))
                    .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                c.PayerName, c.PayorId,
                c.CollectorId, CollectorName(c.CollectorId, collectors),
                c.CollectorId is { } id ? collectors.GetValueOrDefault(id, "Collector") : string.IsNullOrWhiteSpace(c.ActorName) ? "Office" : c.ActorName,
                facilities.Count == 1 ? facilities[0] : null,
                stalls.Count == 0 ? null : string.Join(", ", stalls), null,
                amount, effect, amount + effect, disposition,
                replaces.FirstOrDefault(x => x.Replacement == c.Id)?.OriginalCollectionId,
                detail, correctionDtos));
        }
        return events;
    }
}
