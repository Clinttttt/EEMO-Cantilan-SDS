using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <inheritdoc cref="ICollectorRemittanceRepository" />
public class CollectorRemittanceRepository(AppDbContext context) : ICollectorRemittanceRepository
{
    public async Task<decimal> GetFeeCollectionsTotalAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // The window is the money's own moment, in Philippine time, for the sources that carry a timestamp.
        var (startUtc, _) = PhilippineTime.DayUtcRange(from);
        var (_, endUtc) = PhilippineTime.DayUtcRange(to);

        // ── NPM daily fees, including the fish fee that rides with them ──
        var daily = await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.CollectorId == collectorId
                     && d.IsPaid
                     && (d.UpdatedAt ?? d.CreatedAt) >= startUtc && (d.UpdatedAt ?? d.CreatedAt) < endUtc)
            .Select(d => new { d.DailyFee, d.FishKilos })
            .ToListAsync(ct);

        var npmFishRate = await ResolveNpmFishRateAsync(to, ct);
        var total = daily.Sum(d => d.DailyFee + ((d.FishKilos ?? 0m) * npmFishRate));

        // ── Monthly rentals. Electricity and water are banked separately, so only the fee part counts: a full payment
        //    contributes the rent and any fish fee, and a part payment is applied to that fee charge first and capped
        //    there, the excess belonging to the utilities. ──
        var monthly = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.CollectorId == collectorId
                     && p.Status != PaymentStatus.Unpaid
                     && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc
                     && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc)
            .Select(p => new { p.Status, p.BaseRentalAmount, p.FishKilos, p.PartialAmount })
            .ToListAsync(ct);

        foreach (var p in monthly)
        {
            total += CollectorFeeMoney.MonthlyFeePortion(p.Status, p.BaseRentalAmount, p.FishKilos, p.PartialAmount);
        }

        // ── Slaughterhouse receipts: the transaction's own date IS the day the money was taken. The amount is worked out
        //    from the rate and the head count, so the two columns are read and multiplied here. ──
        var slaughter = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(s => s.CollectorId == collectorId
                     && s.TransactionDate >= from && s.TransactionDate <= to)
            .Select(s => new { s.RatePerHead, s.NumberOfHeads })
            .ToListAsync(ct);
        total += slaughter.Sum(s => s.RatePerHead * s.NumberOfHeads);

        // ── Transport terminal trips ──
        total += await context.TrmTrips
            .AsNoTracking()
            .Where(t => t.CollectorId == collectorId
                     && t.RecordedAt >= startUtc && t.RecordedAt < endUtc)
            .SumAsync(t => t.Fee, ct);

        // ── Tabo-an vendors, whose market day is the day they paid ──
        total += await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.CollectorId == collectorId
                     && a.IsPaid
                     && a.MarketDate >= from && a.MarketDate <= to)
            .SumAsync(a => a.Fee, ct);

        return total;
    }

    public async Task<decimal> GetRemittedTotalAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await Live(collectorId)
            .Where(r => r.CoversFrom <= to && r.CoversTo >= from)
            .SumAsync(r => r.Amount, ct);

    public async Task<CollectorRemittance?> FindOverlappingAsync(
        Guid collectorId, DateOnly from, DateOnly to, Guid? excludingId = null, CancellationToken ct = default)
        => await Live(collectorId)
            .Where(r => r.CoversFrom <= to && r.CoversTo >= from)
            .Where(r => excludingId == null || r.Id != excludingId)
            .OrderBy(r => r.CoversFrom)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CollectorRemittance>> ListAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await Live(collectorId)
            .Where(r => r.CoversFrom <= to && r.CoversTo >= from)
            .OrderBy(r => r.ReceivedAt)
            .ToListAsync(ct);

    public async Task<CollectorRemittance?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.CollectorRemittances.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(CollectorRemittance remittance, CancellationToken ct = default)
        => await context.CollectorRemittances.AddAsync(remittance, ct);

    /// <summary>A voided remittance is money that was never turned in, so it counts for nothing.</summary>
    private IQueryable<CollectorRemittance> Live(Guid collectorId)
        => context.CollectorRemittances
            .AsNoTracking()
            .Where(r => r.CollectorId == collectorId && !r.IsDeleted);

    /// <summary>
    /// The office's own fish fee as of the covered period, falling back to the ordinance constant so Cantilan's figure is
    /// unchanged. Read here rather than passed in, because this repository is the only place that needs it.
    /// </summary>
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
