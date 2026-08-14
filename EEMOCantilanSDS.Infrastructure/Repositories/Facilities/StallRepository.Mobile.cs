using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of StallRepository: what the collector's own app reads about stalls (IStallMobileQueries) - the NPM daily round and
// the monthly-rental round.
//
// The collectable-day arithmetic these depend on lives in StallRepository.Collectable.cs, shared with the stallholders
// register: two copies of it would let the app and the office's paper disagree about what a month owes.
public partial class StallRepository
{
    public async Task<MobileNpmCollectionDto> GetMobileNpmCollectionAsync(int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var effectiveEnd = GetEffectiveCollectionEnd(monthStart, monthEnd, collectionDate);

        // Resolve the municipality's NPM rates as of the collection date (falls back to the ordinance
        // constants, so Cantilan's mobile figures are unchanged).
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        var npmDailyRate = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, collectionDate);
        var npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, collectionDate);

        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Include(s => s.DailyCollections.Where(d =>
                d.CollectionDate >= monthStart &&
                d.CollectionDate <= monthEnd))
            .Where(s =>
                s.Facility!.Code == FacilityCode.NPM &&
                s.Status == StallStatus.Active &&
                (s.Section.HasValue || s.CustomSectionName != null))
            .OrderBy(s => s.Section)
            .ThenBy(s => s.CustomSectionName)
            .ThenBy(s => s.StallNo)
            .ToListAsync(ct);

        // Eligibility: only stalls whose active contract actually covers this collection month. Excludes
        // expired (active-but-lapsed) contracts and stalls with no covering contract — IsActive alone is
        // not enough, since it is a manual flag that does not reflect whether the term has lapsed.
        stalls = stalls.Where(s => s.Contracts.Any(c => c.OverlapsPeriod(monthStart, effectiveEnd))).ToList();

        // The tenant's own market-section display labels (e.g. "Gulayan"), resolved once for the mobile
        // DTO's SectionName. The MarketSection enum stays the logical key; only this mobile display string
        // becomes tenant-aware. Falls back to the canonical section name when no custom label is set.
        var npmFacility = await _context.Facilities.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == FacilityCode.NPM, ct);
        // A canonical section resolves to its tenant label (falling back to the canonical name); a custom
        // section (Section null) shows its per-stall CustomSectionName.
        string SectionDisplay(Stall s)
            => s.Section is { } sec
                ? (npmFacility?.SectionLabel(sec) ?? GetSectionName(sec))
                : (s.CustomSectionName ?? string.Empty);

        var rows = stalls.Select(s =>
        {
            // Prefer the contract that actually covers this collection month — not merely the latest
            // active one — so a future/expired sibling contract can't drive the occupant or the day math.
            var contract = s.Contracts
                .Where(c => c.OverlapsPeriod(monthStart, effectiveEnd))
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault();
            var collectableToday = contract is not null && contract.IsCollectableOn(collectionDate);

            // Through Stall.ResolveDailyFee, the same rule billing, settlement and the reports use: a custom
            // section charges its own rate; a canonical stall charges the tenant's resolved rate even if a legacy
            // figure is still stored on it.
            var dailyRate = s.ResolveDailyFee(npmDailyRate);
            var todayCollection = s.DailyCollections.FirstOrDefault(d => d.CollectionDate == collectionDate);
            var paidCollections = s.DailyCollections
                .Where(d => d.IsPaid && d.CollectionDate >= monthStart && d.CollectionDate <= effectiveEnd)
                .ToList();

            var collectableDays = CountCollectableDays(contract?.EffectivityDate, monthStart,
                contract is not null && contract.ExpiryDate < effectiveEnd ? contract.ExpiryDate : effectiveEnd);
            var daysCollected = paidCollections.Count;
            // Excused/absent days are not owed, so they leave the missed-day count.
            var absentDays = s.DailyCollections.Count(d => d.IsAbsent
                && d.CollectionDate >= monthStart && d.CollectionDate <= effectiveEnd);
            var daysMissed = Math.Max(0, collectableDays - daysCollected - absentDays);
            var monthCollectedAmount = paidCollections.Sum(d =>
                d.DailyFee + (d.FishKilos.GetValueOrDefault() * npmFishRate));

            return new MobileNpmStallCollectionDto(
                s.Id,
                s.StallNo,
                string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                contract?.NameOnContract ?? contract?.ActualOccupant ?? string.Empty,
                s.Section,
                SectionDisplay(s),
                s.Status,
                dailyRate,
                todayCollection is not null,
                todayCollection?.IsPaid == true,
                todayCollection?.ORNumber,
                todayCollection?.FishKilos,
                daysCollected,
                daysMissed,
                collectableDays,
                monthCollectedAmount,
                todayCollection?.IsAbsent == true,
                collectableToday);
        }).ToList();

        var collectedToday = rows.Where(r => r.IsCollectedToday).ToList();
        // "Pending today" = a stall whose contract covers TODAY and hasn't been collected/excused yet —
        // not merely one that has an unpaid day earlier in the month.
        var pendingToday = rows.Where(r => r.IsCollectableToday && !r.IsCollectedToday && !r.IsAbsentToday).ToList();

        return new MobileNpmCollectionDto(
            year,
            month,
            collectionDate,
            rows.Count,
            collectedToday.Count,
            pendingToday.Count,
            collectedToday.Sum(r => r.DailyRate + (r.FishKilosToday.GetValueOrDefault() * npmFishRate)),
            pendingToday.Sum(r => r.DailyRate),
            rows.Sum(r => r.DaysCollected),
            rows.Sum(r => r.DaysMissed),
            rows);
    }

    public async Task<MobileMonthlyCollectionDto> GetMobileMonthlyCollectionAsync(
        FacilityCode facilityCode, int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Include(s => s.PaymentRecords.Where(p =>
                p.BillingYear == year &&
                p.BillingMonth == month))
            .Where(s =>
                s.Facility!.Code == facilityCode &&
                s.Status == StallStatus.Active &&
                s.Contracts.Any(c => c.IsActive))
            .OrderBy(s => s.StallNo)
            .ToListAsync(ct);

        // Eligibility: only stalls whose active contract overlaps the billing month. Excludes expired
        // (active-but-lapsed) contracts — IsActive alone does not reflect whether the term has lapsed.
        stalls = stalls.Where(s => s.Contracts.Any(c => c.OverlapsPeriod(monthStart, monthEnd))).ToList();

        // Which of this month's records were settled online (so the collector sees "paid online" and
        // doesn't collect again). A record is online-settled if it has a Paid/Completed transaction.
        var monthRecordIds = stalls
            .Select(s => s.PaymentRecords.FirstOrDefault())
            .Where(r => r is not null)
            .Select(r => r!.Id)
            .ToList();

        var onlineTxns = monthRecordIds.Count == 0
            ? new List<(Guid PaymentRecordId, Guid Id, OnlinePaymentStatus Status)>()
            : (await _context.OnlinePaymentTransactions
                .AsNoTracking()
                .Where(t => t.PaymentRecordId != null
                    && monthRecordIds.Contains(t.PaymentRecordId.Value)
                    && (t.Status == OnlinePaymentStatus.Paid || t.Status == OnlinePaymentStatus.Completed))
                .Select(t => new { PaymentRecordId = t.PaymentRecordId!.Value, t.Id, t.Status })
                .ToListAsync(ct))
                .Select(t => (t.PaymentRecordId, t.Id, t.Status))
                .ToList();

        // Record ids that were settled online (for the "Online" chip)…
        var onlinePaidRecordIds = onlineTxns.Select(t => t.PaymentRecordId).ToHashSet();
        // …and the still-Paid (not yet OR-completed) transaction per record (for in-field OR encoding).
        var awaitingOrTxnByRecord = onlineTxns
            .Where(t => t.Status == OnlinePaymentStatus.Paid)
            .GroupBy(t => t.PaymentRecordId)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var rows = stalls.Select(s =>
        {
            var contract = s.Contracts
                .Where(c => c.OverlapsPeriod(monthStart, monthEnd))
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault();

            var record = s.PaymentRecords.FirstOrDefault();
            var status = record?.Status ?? PaymentStatus.Unpaid;
            // Monthly-rental facilities carry no utilities, so the bill is the flat monthly rate.
            var amountPaid = record?.AmountPaid ?? 0m;
            var balance = record is not null ? record.BalanceDue : s.MonthlyRate;

            var paidOnline = record is not null && onlinePaidRecordIds.Contains(record.Id);
            // Paid online but the staff have not yet encoded the Official Receipt (no OR on the record).
            var awaitingOr = paidOnline && string.IsNullOrWhiteSpace(record!.ORNumber);
            Guid? onlineTxnId = awaitingOr && awaitingOrTxnByRecord.TryGetValue(record!.Id, out var txnId)
                ? txnId
                : null;

            return new MobileMonthlyStallCollectionDto(
                s.Id,
                s.StallNo,
                string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                contract?.NameOnContract ?? contract?.ActualOccupant ?? string.Empty,
                GetMonthlyAreaLabel(s),
                s.MonthlyRate,
                status,
                amountPaid,
                balance,
                record?.ORNumber,
                record is not null,
                paidOnline,
                awaitingOr,
                onlineTxnId);
        }).ToList();

        // Facility display name from the seeded Facility record (single source of truth).
        var facilityName = await _context.Facilities
            .AsNoTracking()
            .Where(f => f.Code == facilityCode)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(ct) ?? facilityCode.ToString();

        return new MobileMonthlyCollectionDto(
            facilityCode,
            facilityName,
            year,
            month,
            collectionDate,
            rows.Count,
            rows.Count(r => r.Status == PaymentStatus.Paid),
            rows.Count(r => r.Status == PaymentStatus.Partial),
            rows.Count(r => r.Status == PaymentStatus.Unpaid),
            rows.Sum(r => r.AmountPaid),
            rows.Sum(r => r.Balance),
            rows);
    }

    private static string GetMonthlyAreaLabel(Stall s)
    {
        if (s.AreaLocation.HasValue)
            return s.AreaLocation.Value.ToString();
        if (s.Section.HasValue)
            return GetSectionName(s.Section);
        // No generic stall-type chip ("Permanent"/"Transient") — it adds noise on the collector card.
        return string.Empty;
    }
}
