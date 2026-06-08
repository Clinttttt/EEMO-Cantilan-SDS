using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
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
public class TransactionFeedRepository(AppDbContext context) : ITransactionFeedRepository
{
    public async Task<IReadOnlyList<TransactionFeedDto>> GetRecentTransactionsAsync(
        FacilityCode? facility, DateOnly? onDate, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) limit = 100;
        var all = facility is null;
        var results = new List<TransactionFeedDto>();

        if (all || facility is FacilityCode.NPM or FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE)
            results.AddRange(await StallPaymentRowsAsync(facility, onDate, limit, ct));

        if (all || facility is FacilityCode.NPM)
            results.AddRange(await DailyCollectionRowsAsync(onDate, limit, ct));

        if (all || facility is FacilityCode.SLH)
            results.AddRange(await SlaughterRowsAsync(onDate, limit, ct));

        if (all || facility is FacilityCode.TRM)
            results.AddRange(await TripRowsAsync(onDate, limit, ct));

        if (all || facility is FacilityCode.TPM)
            results.AddRange(await AttendanceRowsAsync(onDate, limit, ct));

        return results
            .OrderByDescending(r => r.OccurredAt)
            .Take(limit)
            .ToList();
    }

    private async Task<List<TransactionFeedDto>> StallPaymentRowsAsync(FacilityCode? facility, DateOnly? onDate, int limit, CancellationToken ct)
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
                Occupant = p.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                p.Status,
                p.BaseRentalAmount,
                p.PartialAmount,
                p.ElecAmount,
                p.WaterAmount,
                p.FishKilos,
                p.ORNumber,
                p.BillingYear,
                p.BillingMonth,
                When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var total = r.BaseRentalAmount + (r.ElecAmount ?? 0) + (r.WaterAmount ?? 0)
                        + (r.FishKilos.HasValue ? r.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0);
            var amount = r.Status == PaymentStatus.Paid ? total
                       : r.Status == PaymentStatus.Partial ? r.PartialAmount
                       : 0m;
            var period = new DateOnly(r.BillingYear, r.BillingMonth, 1).ToString("MMM yyyy");
            return new TransactionFeedDto(
                r.Id, r.Code, r.FacilityName, PhilippineTime.ToPhilippineTime(r.When), true,
                string.IsNullOrWhiteSpace(r.Occupant) ? "Stall " + r.StallNo : r.Occupant!,
                $"Stall {r.StallNo} · for {period}",
                "Monthly Rent", amount, r.ORNumber,
                r.Status == PaymentStatus.Paid ? "Paid" : "Partial");
        }).ToList();
    }

    private async Task<List<TransactionFeedDto>> DailyCollectionRowsAsync(DateOnly? onDate, int limit, CancellationToken ct)
    {
        var q = context.DailyCollections.AsNoTracking().Where(d => d.IsPaid);
        if (onDate is { } d)
            q = q.Where(x => x.CollectionDate == d);

        var rows = await q
            .OrderByDescending(d => d.CollectionDate)
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                Code = d.Stall!.Facility!.Code,
                FacilityName = d.Stall.Facility.Name,
                d.Stall.StallNo,
                Occupant = d.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                d.DailyFee,
                d.FishKilos,
                d.ORNumber,
                d.CollectionDate
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var amount = r.DailyFee + (r.FishKilos.HasValue ? r.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0);
            return new TransactionFeedDto(
                r.Id, r.Code, r.FacilityName, r.CollectionDate.ToDateTime(TimeOnly.MinValue), false,
                string.IsNullOrWhiteSpace(r.Occupant) ? "Stall " + r.StallNo : r.Occupant!,
                $"Stall {r.StallNo}",
                "Daily Fee", amount, r.ORNumber, "Paid");
        }).ToList();
    }

    private async Task<List<TransactionFeedDto>> SlaughterRowsAsync(DateOnly? onDate, int limit, CancellationToken ct)
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
                s.TransactionDate
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var animal = string.IsNullOrWhiteSpace(r.CustomAnimalType) ? r.AnimalType.ToString() : r.CustomAnimalType!;
            return new TransactionFeedDto(
                r.Id, FacilityCode.SLH, string.IsNullOrWhiteSpace(r.FacilityName) ? "Slaughterhouse" : r.FacilityName,
                r.TransactionDate.ToDateTime(TimeOnly.MinValue), false,
                r.OwnerName,
                $"{animal} ×{r.NumberOfHeads}",
                "Slaughter", r.RatePerHead * r.NumberOfHeads, r.ORNumber, "Paid");
        }).ToList();
    }

    private async Task<List<TransactionFeedDto>> TripRowsAsync(DateOnly? onDate, int limit, CancellationToken ct)
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
                When = t.RecordedAt
            })
            .ToListAsync(ct);

        return rows.Select(r => new TransactionFeedDto(
            r.Id, FacilityCode.TRM, "Transport Terminal", PhilippineTime.ToPhilippineTime(r.When), true,
            r.DriverName,
            $"{r.PlateNumber} · {r.Route}",
            "Terminal Trip", r.Fee, r.ORNumber, "Paid")).ToList();
    }

    private async Task<List<TransactionFeedDto>> AttendanceRowsAsync(DateOnly? onDate, int limit, CancellationToken ct)
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
                a.MarketDate
            })
            .ToListAsync(ct);

        return rows.Select(r => new TransactionFeedDto(
            r.Id, FacilityCode.TPM, "Tabo-an Public Market", r.MarketDate.ToDateTime(TimeOnly.MinValue), false,
            r.VendorName,
            r.Goods,
            "Market Day", r.Fee, r.ORNumber, "Paid")).ToList();
    }
}
