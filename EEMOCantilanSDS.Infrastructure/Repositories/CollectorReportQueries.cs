using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <inheritdoc cref="ICollectorReportQueries" />
public class CollectorReportQueries(AppDbContext context) : ICollectorReportQueries
{
    public async Task<CollectorCollectionsData> GetCollectionsAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (startUtc, _) = PhilippineTime.DayUtcRange(from);
        var (_, endUtc) = PhilippineTime.DayUtcRange(to);

        var lines = new List<CollectorCollectionLine>();
        var absences = new List<CollectorAbsenceLine>();

        // ── NPM daily fees. The fee's own day is kept, since a receipt may answer for days the payor owed. ──
        var npmFishRate = await ResolveNpmFishRateAsync(to, ct);
        var daily = await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.CollectorId == collectorId
                     && (d.IsPaid || d.IsAbsent)
                     && (d.UpdatedAt ?? d.CreatedAt) >= startUtc && (d.UpdatedAt ?? d.CreatedAt) < endUtc)
            .Select(d => new
            {
                d.ORNumber,
                d.CollectionDate,
                d.DailyFee,
                d.FishKilos,
                d.IsAbsent,
                d.Stall!.StallNo,
                Code = d.Stall.Facility!.Code,
                Contracts = d.Stall.Contracts.Select(c => new { c.ActualOccupant, c.NameOnContract, c.EffectivityDate }).ToList(),
                When = d.UpdatedAt ?? d.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var d in daily)
        {
            var payor = d.Contracts
                .OrderByDescending(c => c.EffectivityDate)
                .Select(c => string.IsNullOrWhiteSpace(c.ActualOccupant) ? c.NameOnContract : c.ActualOccupant)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "No active occupant";

            if (d.IsAbsent)
            {
                absences.Add(new CollectorAbsenceLine(d.CollectionDate, payor!, d.StallNo, d.Code));
                continue;
            }

            lines.Add(new CollectorCollectionLine(
                d.ORNumber, d.When, payor!, d.StallNo, d.Code, "Daily Fee",
                d.DailyFee + ((d.FishKilos ?? 0m) * npmFishRate), d.CollectionDate, null));
        }

        // ── Monthly rentals. Fee money only: the meters are banked apart and are totalled separately below. ──
        var monthly = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.CollectorId == collectorId
                     && p.Status != PaymentStatus.Unpaid
                     && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc
                     && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc)
            .Select(p => new
            {
                p.ORNumber,
                p.Status,
                p.BaseRentalAmount,
                p.PartialAmount,
                p.FishKilos,
                p.BillingYear,
                p.BillingMonth,
                p.Stall!.StallNo,
                Code = p.Stall.Facility!.Code,
                Contracts = p.Stall.Contracts.Select(c => new { c.ActualOccupant, c.NameOnContract, c.EffectivityDate }).ToList(),
                When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var p in monthly)
        {
            var payor = p.Contracts
                .OrderByDescending(c => c.EffectivityDate)
                .Select(c => string.IsNullOrWhiteSpace(c.ActualOccupant) ? c.NameOnContract : c.ActualOccupant)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "No active occupant";

            lines.Add(new CollectorCollectionLine(
                p.ORNumber, p.When, payor!, p.StallNo, p.Code, "Stall Rental",
                CollectorFeeMoney.MonthlyFeePortion(p.Status, p.BaseRentalAmount, p.FishKilos, p.PartialAmount),
                null,
                new DateOnly(p.BillingYear, p.BillingMonth, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)));
        }

        // ── Slaughterhouse: one line per animal type on the receipt, its own date being the day it was taken ──
        var slaughter = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(s => s.CollectorId == collectorId && s.TransactionDate >= from && s.TransactionDate <= to)
            .Select(s => new { s.ORNumber, s.OwnerName, s.AnimalType, s.RatePerHead, s.NumberOfHeads, s.TransactionDate, When = s.UpdatedAt ?? s.CreatedAt })
            .ToListAsync(ct);

        lines.AddRange(slaughter.Select(s => new CollectorCollectionLine(
            s.ORNumber, s.When, s.OwnerName, null, FacilityCode.SLH,
            $"{s.AnimalType} × {s.NumberOfHeads}", s.RatePerHead * s.NumberOfHeads, s.TransactionDate, null)));

        // ── Terminal trips ──
        var trips = await context.TrmTrips
            .AsNoTracking()
            .Where(t => t.CollectorId == collectorId && t.RecordedAt >= startUtc && t.RecordedAt < endUtc)
            .Select(t => new { t.ORNumber, t.DriverName, t.PlateNumber, t.Fee, t.RecordedAt })
            .ToListAsync(ct);

        lines.AddRange(trips.Select(t => new CollectorCollectionLine(
            t.ORNumber, t.RecordedAt, t.DriverName, t.PlateNumber, FacilityCode.TRM,
            "Trip Fee", t.Fee, DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(t.RecordedAt)), null)));

        // ── Tabo-an vendors, whose market day is the day they paid ──
        var taboan = await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.CollectorId == collectorId && a.IsPaid && a.MarketDate >= from && a.MarketDate <= to)
            .Select(a => new { a.ORNumber, a.Vendor!.VendorName, a.Fee, a.MarketDate, When = a.UpdatedAt ?? a.CreatedAt })
            .ToListAsync(ct);

        lines.AddRange(taboan.Select(a => new CollectorCollectionLine(
            a.ORNumber, a.When, a.VendorName, null, FacilityCode.TPM,
            "Vendor Fee", a.Fee, a.MarketDate, null)));

        // ── What the office itself recorded at this collector's facilities, stated apart from their own accountability ──
        var assigned = await context.CollectorFacilityAssignments
            .AsNoTracking()
            .Where(a => a.CollectorId == collectorId)
            .Select(a => a.FacilityCode)
            .ToListAsync(ct);

        var officeDaily = await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.CollectorId == null && d.IsPaid
                     && (d.UpdatedAt ?? d.CreatedAt) >= startUtc && (d.UpdatedAt ?? d.CreatedAt) < endUtc
                     && assigned.Contains(d.Stall!.Facility!.Code))
            .Select(d => new { d.DailyFee, d.FishKilos })
            .ToListAsync(ct);

        var officeRecorded = officeDaily.Sum(d => d.DailyFee + ((d.FishKilos ?? 0m) * npmFishRate));
        var officeReceipts = officeDaily.Count;

        // ── Electricity and water this collector took, kept in their own totals ──
        var bills = await context.UtilityBills
            .AsNoTracking()
            .Where(b => b.CollectorId == collectorId
                     && (b.UpdatedAt ?? b.CreatedAt) >= startUtc && (b.UpdatedAt ?? b.CreatedAt) < endUtc)
            .Select(b => new
            {
                b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount,
                b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount
            })
            .ToListAsync(ct);

        decimal utilityBilled = 0m, utilityCollected = 0m;
        foreach (var b in bills)
        {
            var elecCharge = Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh;
            var waterCharge = Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter;
            utilityBilled += elecCharge + waterCharge;
            utilityCollected += Collected(b.ElecStatus, elecCharge, b.ElecPartialAmount)
                             + Collected(b.WaterStatus, waterCharge, b.WaterPartialAmount);
        }

        return new CollectorCollectionsData(
            lines.OrderBy(l => l.TakenAtUtc).ToList(),
            absences.OrderBy(a => a.Day).ToList(),
            officeRecorded,
            officeReceipts,
            utilityBilled,
            utilityCollected);

        static decimal Collected(PaymentStatus status, decimal charge, decimal partial) => status switch
        {
            PaymentStatus.Paid => charge,
            PaymentStatus.Partial => partial,
            _ => 0m
        };
    }

    /// <summary>The office's own fish fee as of the period, falling back to the ordinance constant.</summary>
    private async Task<decimal> ResolveNpmFishRateAsync(DateOnly asOf, CancellationToken ct)
    {
        var rate = await context.FacilityRates
            .AsNoTracking()
            .Where(r => r.FacilityCode == FacilityCode.NPM
                     && r.RateKey == FeeRateKey.NpmFishPerKilo
                     && r.EffectiveDate <= asOf
                     && !r.IsDeleted)
            .OrderByDescending(r => r.EffectiveDate)
            .Select(r => (decimal?)r.Amount)
            .FirstOrDefaultAsync(ct);

        return rate ?? Domain.Constants.FeeRates.NpmFishFeePerKilo;
    }
}
