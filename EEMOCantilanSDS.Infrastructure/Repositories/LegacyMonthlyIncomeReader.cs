using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// The legacy half of the official Monthly Income (IA-051): cash whose authoritative record is still a specialized source.
/// Each source is read once, and a source row whose settlement authority has become Canonical is excluded, because its
/// Collection line is the authoritative money and its legacy fields are only a compatibility projection. Legacy-only
/// sources (daily collections, slaughterhouse, terminal, Tabo) have no canonical twin to double count.
/// </summary>
public sealed class LegacyMonthlyIncomeReader(AppDbContext context) : ILegacyMonthlyIncomeReader
{
    public async Task<IReadOnlyList<LegacyIncomeFact>> GetAsync(int year, CancellationToken ct = default)
    {
        var (startUtc, _) = PhilippineTime.MonthUtcRange(year, 1);
        var (_, endUtc) = PhilippineTime.MonthUtcRange(year, 12);
        var facts = new List<LegacyIncomeFact>();
        static int MonthOf(DateTime utc) => PhilippineTime.ToPhilippineTime(utc).Month;
        var fishRate = await context.FacilityRates.AsNoTracking()
            .Where(r => r.FacilityCode == FacilityCode.NPM && r.RateKey == FeeRateKey.NpmFishPerKilo && !r.IsDeleted)
            .OrderByDescending(r => r.EffectiveDate).Select(r => (decimal?)r.Amount).FirstOrDefaultAsync(ct)
            ?? FeeRates.NpmFishFeePerKilo;

        // NPM daily collections: the daily fee is stall rent; weighing money is Weight and Measure. New Fish rows carry a
        // frozen amount; an older row without one is priced at the Fish rate read now, which is the legacy reading and is
        // never presented as frozen evidence.
        var daily = await context.DailyCollections.AsNoTracking()
            .Where(d => d.IsPaid && (d.UpdatedAt ?? d.CreatedAt) >= startUtc && (d.UpdatedAt ?? d.CreatedAt) < endUtc)
            .Select(d => new
            {
                When = d.UpdatedAt ?? d.CreatedAt, d.DailyFee, d.FishKilos, d.FishFeeAmountFrozen, d.MeatFeeAmount,
                Code = d.Stall!.Facility!.Code
            }).ToListAsync(ct);
        foreach (var d in daily)
        {
            var month = MonthOf(d.When);
            facts.Add(new(month, RevenueClassificationCodes.PermanentStallRent, d.Code, d.DailyFee, "DailyCollection"));
            var fish = d.FishFeeAmountFrozen ?? ((d.FishKilos ?? 0m) * fishRate);
            if (fish + d.MeatFeeAmount != 0m)
                facts.Add(new(month, RevenueClassificationCodes.WeightAndMeasure, null, fish + d.MeatFeeAmount, "DailyCollection.Weighing"));
        }

        // Monthly rentals: the fee portion of what was paid, by the month it was paid. A converted row is excluded.
        var monthly = await context.PaymentRecords.AsNoTracking()
            .Where(p => p.Status != PaymentStatus.Unpaid && p.SettlementAuthorityState != SettlementAuthority.Canonical
                && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc)
            .Select(p => new
            {
                When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt, p.Status, p.BaseRentalAmount, p.PartialAmount, p.FishKilos,
                Code = p.Stall!.Facility!.Code
            }).ToListAsync(ct);
        foreach (var p in monthly)
        {
            var portion = EEMOCantilanSDS.Application.Common.Fees.CollectorFeeMoney.MonthlyFeePortion(
                p.Status, p.BaseRentalAmount, p.FishKilos, p.PartialAmount, fishRate);
            var rent = Math.Min(portion, p.BaseRentalAmount);
            var month = MonthOf(p.When);
            var classification = p.Code == FacilityCode.ICE ? RevenueClassificationCodes.IcePlant : RevenueClassificationCodes.PermanentStallRent;
            facts.Add(new(month, classification, p.Code, rent, "PaymentRecord"));
            if (portion - rent > 0m)
                facts.Add(new(month, RevenueClassificationCodes.WeightAndMeasure, null, portion - rent, "PaymentRecord.Weighing"));
        }

        // Electricity and water, each by its own settlement authority. Converted parts are excluded.
        var bills = await context.UtilityBills.AsNoTracking()
            .Where(b => (b.UpdatedAt ?? b.CreatedAt) >= startUtc && (b.UpdatedAt ?? b.CreatedAt) < endUtc)
            .Select(b => new
            {
                b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount, b.ElecPaidAt,
                b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount, b.WaterPaidAt,
                b.ElectricitySettlementAuthorityState, b.WaterSettlementAuthorityState, When = b.UpdatedAt ?? b.CreatedAt
            }).ToListAsync(ct);
        foreach (var b in bills)
        {
            var elec = Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh;
            var water = Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter;
            if (CollectionSourceAuthorityMap.LegacyMoneyCounts(CollectionSourceKind.UtilityBill, b.ElectricitySettlementAuthorityState))
            {
                var paid = Paid(b.ElecStatus, elec, b.ElecPartialAmount);
                if (paid > 0m) facts.Add(new(MonthOf(b.ElecPaidAt ?? b.When), RevenueClassificationCodes.Ecf, null, paid, "UtilityBill.Electricity"));
            }
            if (CollectionSourceAuthorityMap.LegacyMoneyCounts(CollectionSourceKind.UtilityBill, b.WaterSettlementAuthorityState))
            {
                var paid = Paid(b.WaterStatus, water, b.WaterPartialAmount);
                if (paid > 0m) facts.Add(new(MonthOf(b.WaterPaidAt ?? b.When), RevenueClassificationCodes.Wcf, null, paid, "UtilityBill.Water"));
            }
        }

        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var slaughter = await context.SlaughterTransactions.AsNoTracking()
            .Where(s => s.TransactionDate >= first && s.TransactionDate <= last)
            .Select(s => new { s.TransactionDate, Amount = s.RatePerHead * s.NumberOfHeads }).ToListAsync(ct);
        facts.AddRange(slaughter.Select(s => new LegacyIncomeFact(s.TransactionDate.Month, RevenueClassificationCodes.Slaughterhouse, null, s.Amount, "SlaughterTransaction")));

        var trips = await context.TrmTrips.AsNoTracking()
            .Where(t => t.RecordedAt >= startUtc && t.RecordedAt < endUtc).Select(t => new { t.RecordedAt, t.Fee }).ToListAsync(ct);
        facts.AddRange(trips.Select(t => new LegacyIncomeFact(MonthOf(t.RecordedAt), RevenueClassificationCodes.TransportationParking, null, t.Fee, "TrmTrip")));

        var tabo = await context.TpmAttendances.AsNoTracking()
            .Where(a => a.IsPaid && a.MarketDate >= first && a.MarketDate <= last).Select(a => new { a.MarketDate, a.Fee }).ToListAsync(ct);
        facts.AddRange(tabo.Select(a => new LegacyIncomeFact(a.MarketDate.Month, RevenueClassificationCodes.Tabo, null, a.Fee, "TpmAttendance")));

        return facts;

        static decimal Paid(PaymentStatus status, decimal charge, decimal partial) => status switch
        {
            PaymentStatus.Paid => charge,
            PaymentStatus.Partial => partial,
            _ => 0m
        };
    }
}
