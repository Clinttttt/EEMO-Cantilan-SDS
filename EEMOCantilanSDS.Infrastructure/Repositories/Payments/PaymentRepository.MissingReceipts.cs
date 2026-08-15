using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of PaymentRepository: money the office HAS taken whose Official Receipt is still blank (IMissingReceiptQueries).
//
// A different question from what an account owes. These scan a whole period across every payor, because a payment without a
// receipt number is a payment the office cannot account for at audit - a follow-up task, not a balance. Each row is named after
// the lessee answerable for its own billing month, not whoever holds the stall now.
public partial class PaymentRepository
{
    public async Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsAsync(int year, int month, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Resolve the municipality's NPM fish rate as of the period (constant fallback → Cantilan unchanged).
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        var npmFish = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, monthStart);

        // Online payments also leave ORNumber null until staff encode it, but they have their own
        // awaiting-OR queue (keyed by an OnlinePaymentTransaction). Exclude them here so a payment is
        // never listed under both "online awaiting OR" and "cash awaiting OR".
        var onlineRecordIds = _context.OnlinePaymentTransactions.Select(t => t.PaymentRecordId);

        // ── Monthly: fully-Paid records with a blank OR (cash/field), for the selected billing period ──
        // Restricted to Paid (not Partial) so this never overlaps the current-period "Partial" follow-up.
        var monthlyRaw = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.BillingYear == year && p.BillingMonth == month
                && p.Status == PaymentStatus.Paid
                && (p.ORNumber == null || p.ORNumber == "")
                && !onlineRecordIds.Contains(p.Id))
            .Select(p => new
            {
                Code = p.Stall!.Facility!.Code,
                p.Stall.StallNo,
                p.StallId,
                Occupant = p.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                p.BaseRentalAmount,
                p.ElecAmount,
                p.WaterAmount,
                p.FishKilos
            })
            .ToListAsync(ct);

        var monthly = monthlyRaw.Select(p => new UnreceiptedPaymentDto(
            p.Code,
            p.StallNo,
            string.IsNullOrWhiteSpace(p.Occupant) ? string.Empty : p.Occupant!,
            p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0)
                + (p.FishKilos.HasValue ? p.FishKilos.Value * npmFish : 0m),
            1,
            IsDaily: false,
            StallId: p.StallId,
            Year: year,
            Month: month))
            .ToList();

        // ── NPM daily: paid daily collections with a blank OR, grouped per stall for the period ──
        var dailyRaw = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.IsPaid
                && (dc.ORNumber == null || dc.ORNumber == "")
                && dc.CollectionDate >= monthStart && dc.CollectionDate <= monthEnd)
            .Select(dc => new
            {
                Code = dc.Stall!.Facility!.Code,
                dc.Stall.StallNo,
                dc.StallId,
                Occupant = dc.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                dc.DailyFee,
                dc.FishKilos
            })
            .ToListAsync(ct);

        var daily = dailyRaw
            .GroupBy(d => new { d.Code, d.StallNo, d.Occupant })
            .Select(g => new UnreceiptedPaymentDto(
                g.Key.Code,
                g.Key.StallNo,
                string.IsNullOrWhiteSpace(g.Key.Occupant) ? string.Empty : g.Key.Occupant!,
                // Daily fee only — the ₱1/kg fish surcharge is tracked separately (as in the NPM/Export
                // reports), so it is excluded from this "Daily receipt · OR" amount.
                g.Sum(x => x.DailyFee),
                g.Count(),
                IsDaily: true,
                StallId: g.First().StallId,
                Year: year,
                Month: month))
            .ToList();

        // Name every row after the lessee answerable for that billing month. The projections above read the stall's
        // CURRENT contract, which on a stall since re-let is somebody else: a former lessee's receipt would then be
        // listed, and chased, under the sitting lessee's name.
        var rows = monthly.Concat(daily).ToList();
        return await WithAnswerableOccupantsAsync(rows, ct);
    }

    /// <summary>
    /// Re-labels receipt rows with the occupant answerable for each row's own billing month. Rows whose stall or term
    /// cannot be resolved keep whatever name they came with, so a row is never left blank.
    /// </summary>
    private async Task<IReadOnlyList<UnreceiptedPaymentDto>> WithAnswerableOccupantsAsync(
        List<UnreceiptedPaymentDto> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return rows;

        var stallIds = rows.Where(r => r.StallId != Guid.Empty).Select(r => r.StallId).Distinct().ToList();
        if (stallIds.Count == 0)
            return rows;

        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Where(s => stallIds.Contains(s.Id))
            .ToListAsync(ct);

        var directory = OccupantDirectory.From(stalls, _clock.PhilippineToday);

        return rows
            .Select(r =>
            {
                if (r.StallId == Guid.Empty || r.Year <= 0 || r.Month is < 1 or > 12)
                    return r;

                var answerable = directory.InMonth(r.StallId, r.Year, r.Month);
                return string.IsNullOrWhiteSpace(answerable) ? r : r with { Occupant = answerable! };
            })
            .ToList();
    }

    /// <summary>
    /// Whole-year variant of <see cref="GetUnreceiptedCashPaymentsAsync"/>: every fully-paid cash/field
    /// record for the year that still lacks an OR, one row per (stall, billing month). Used by the
    /// Follow-up History "Whole year" view so a blank-OR settlement in ANY month surfaces under Missing OR
    /// (the single-month path is unchanged). Online payments are excluded (they have their own queue).
    /// </summary>
    public async Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsForYearAsync(int year, CancellationToken ct)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        var npmFish = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, yearStart);

        var onlineRecordIds = _context.OnlinePaymentTransactions.Select(t => t.PaymentRecordId);

        // ── Monthly: fully-Paid records with a blank OR, any billing month of the year ──
        var monthlyRaw = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.BillingYear == year
                && p.Status == PaymentStatus.Paid
                && (p.ORNumber == null || p.ORNumber == "")
                && !onlineRecordIds.Contains(p.Id))
            .Select(p => new
            {
                Code = p.Stall!.Facility!.Code,
                p.Stall.StallNo,
                p.StallId,
                Occupant = p.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                p.BillingMonth,
                p.BaseRentalAmount,
                p.ElecAmount,
                p.WaterAmount,
                p.FishKilos
            })
            .ToListAsync(ct);

        var monthly = monthlyRaw.Select(p => new UnreceiptedPaymentDto(
            p.Code,
            p.StallNo,
            string.IsNullOrWhiteSpace(p.Occupant) ? string.Empty : p.Occupant!,
            p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0)
                + (p.FishKilos.HasValue ? p.FishKilos.Value * npmFish : 0m),
            1,
            IsDaily: false,
            StallId: p.StallId,
            Year: year,
            Month: p.BillingMonth));

        // ── NPM daily: paid blank-OR days, grouped per (stall, calendar month) of the year ──
        var dailyRaw = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.IsPaid
                && (dc.ORNumber == null || dc.ORNumber == "")
                && dc.CollectionDate >= yearStart && dc.CollectionDate <= yearEnd)
            .Select(dc => new
            {
                Code = dc.Stall!.Facility!.Code,
                dc.Stall.StallNo,
                dc.StallId,
                Occupant = dc.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                Month = dc.CollectionDate.Month,
                dc.DailyFee,
                dc.FishKilos
            })
            .ToListAsync(ct);

        var daily = dailyRaw
            .GroupBy(d => new { d.Code, d.StallNo, d.Occupant, d.Month })
            .Select(g => new UnreceiptedPaymentDto(
                g.Key.Code,
                g.Key.StallNo,
                string.IsNullOrWhiteSpace(g.Key.Occupant) ? string.Empty : g.Key.Occupant!,
                g.Sum(x => x.DailyFee),
                g.Count(),
                IsDaily: true,
                StallId: g.First().StallId,
                Year: year,
                Month: g.Key.Month));

        // Each row is named after the lessee answerable for its own billing month, not the stall's current one.
        return await WithAnswerableOccupantsAsync(monthly.Concat(daily).ToList(), ct);
    }
}
