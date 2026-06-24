using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of FacilityReportsRepository: stall-compliance row helpers.
public partial class FacilityReportsRepository
{
    #region Stall Compliance Helpers

    /// <summary>
    /// Per-stall compliance rows for the report page (powers both the delinquency table
    /// and the full "all stalls" table). Covers occupied stalls (active contract) only.
    /// Status/balance reflect the selected period; MissedMonths counts months in the
    /// period year (up to the period month) the stall was under contract yet paid nothing
    /// (NPM recognises daily collections; see <see cref="CountMissedMonths"/>).
    /// </summary>
    private async Task<IReadOnlyList<StallComplianceDto>> GenerateStallComplianceAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var stalls = (await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.FacilityId == facilityId
               
                && s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct))
            // Only stalls whose contract was actually effective during the selected period.
            // Without this, a stall whose contract starts after the period (or expired before it)
            // appears with a ₱0 obligation and renders as a phantom "Paid" payor for a month it
            // did not yet operate — e.g. viewing May when every contract begins June 5.
            .Where(s => CountNpmCollectableDays(s, startDate, endDate) > 0)
            .ToList();

        if (stalls.Count == 0)
            return Array.Empty<StallComplianceDto>();

        var stallIds = stalls.Select(s => s.Id).ToList();

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId))
            .ToListAsync(ct);

        var includeFish = facilityCode == FacilityCode.NPM;
        var (complianceStart, complianceEnd) = (startDate, endDate);

        var periodPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, complianceStart, complianceEnd))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).ToList());

        var stallsWithNpmPeriodPayments = includeFish
            ? periodPayments
                .Where(kvp => kvp.Value.Any(pr => pr.Status != PaymentStatus.Unpaid))
                .Select(kvp => kvp.Key)
                .ToHashSet()
            : new HashSet<Guid>();

        var dailyByStall = includeFish
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && dc.IsPaid
                    && !stallsWithNpmPeriodPayments.Contains(dc.StallId)
                    && dc.CollectionDate >= complianceStart && dc.CollectionDate <= complianceEnd)
                .GroupBy(dc => dc.StallId)
                .Select(g => new { StallId = g.Key, Total = g.Sum(dc => dc.DailyFee) })
                .ToDictionaryAsync(x => x.StallId, x => x.Total, ct)
            : new Dictionary<Guid, decimal>();

        // Months (this year, up to the report month) in which each NPM stall recorded at least one
        // paid daily collection. NPM is collected daily, so a daily collection — not a monthly
        // "Paid" PaymentRecord — is the real evidence that a month was paid. Used by CountMissedMonths.
        var yearStart = new DateOnly(endDate.Year, 1, 1);
        var dailyPaidMonthsByStall = includeFish
            ? (await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => stallIds.Contains(dc.StallId) && dc.IsPaid
                        && dc.CollectionDate >= yearStart && dc.CollectionDate <= endDate)
                    .Select(dc => new { dc.StallId, dc.CollectionDate })
                    .ToListAsync(ct))
                .GroupBy(x => x.StallId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate.Month).ToHashSet())
            : new Dictionary<Guid, HashSet<int>>();

        var rows = new List<StallComplianceDto>();

        foreach (var s in stalls)
        {
            var contract = s.Contracts.FirstOrDefault(c => c.IsActive);

            decimal totalBill;
            decimal rentBill;
            string? orNumber = null;
            decimal amountPaid;

            // For NPM, the monthly record is the monthly equivalent of a daily ₱30 obligation.
            // The compliance balance is always selected-period obligation minus selected-period collections.
            if (includeFish)
            {
                var npmPayments = periodPayments.GetValueOrDefault(s.Id) ?? new List<PaymentRecord>();
                // Occupancy-prorated rent: collectable days in the period × ₱30 (counts from the
                // contract's effectivity date, so a payor who started mid-month owes only their days).
                rentBill = CalculateNpmDailyObligation(s, complianceStart, complianceEnd);
                totalBill = rentBill
                    + npmPayments.Sum(pr => CalculateNpmAdditionalCharges(pr, complianceStart, complianceEnd));
                amountPaid = npmPayments.Sum(pr => RecognizedNpmPaymentRevenue(pr, complianceStart, complianceEnd, s))
                    + dailyByStall.GetValueOrDefault(s.Id);
                orNumber = npmPayments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else if (periodPayments.TryGetValue(s.Id, out var payments) && payments.Count > 0)
            {
                // Monthly-billed facilities (TCC/NCC/BBQ/ICE): the bill is the FULL rent obligation
                // due across every month the contract is effective in the period (so unpaid months
                // without a record still count), plus any utilities actually billed on in-period
                // records. The balance then reconciles with MissedMonths and the Collection Rate.
                rentBill = CalculateStallRentObligationDue(s, complianceStart, complianceEnd);
                totalBill = rentBill
                    + payments.Sum(pr => (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0));
                amountPaid = payments.Sum(pr => pr.Status == PaymentStatus.Paid
                    ? pr.BaseRentalAmount + (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0)
                    : pr.Status == PaymentStatus.Partial ? pr.PartialAmount : 0m);
                orNumber = payments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else
            {
                // No payment record in the period. Monthly facilities still owe the full rent
                // obligation that has come due across the period (every effective, started month —
                // not just one). NPM has no monthly record here either, so its daily collections
                // (dailyByStall) settle against its own daily obligation.
                rentBill = includeFish
                    ? CalculateNpmDailyObligation(s, complianceStart, complianceEnd)
                    : CalculateStallRentObligationDue(s, complianceStart, complianceEnd);
                totalBill = rentBill;
                amountPaid = dailyByStall.GetValueOrDefault(s.Id);
            }

            var balance = Math.Max(0m, totalBill - amountPaid);
            var status = balance <= 0m ? "Paid" : amountPaid > 0m ? "Partial" : "Unpaid";

            var missedMonths = CountMissedMonths(
                paymentRecords, s, endDate, includeFish, dailyPaidMonthsByStall.GetValueOrDefault(s.Id));

            rows.Add(new StallComplianceDto(
                s.Id,
                s.StallNo,
                contract?.ActualOccupant ?? string.Empty,
                contract?.NameOnContract ?? string.Empty,
                s.Section.HasValue ? SectionLabel(s.Section) : s.AreaLocation?.ToString() ?? string.Empty,
                s.Type.ToString(),
                s.MonthlyRate,
                s.DailyRate ?? (includeFish ? FeeRates.NpmDailyFee : 0m),
                status,
                amountPaid,
                balance,
                orNumber,
                missedMonths,
                s.AreaSqm ?? 0,
                contract?.EffectivityDate,
                contract?.DurationYears ?? 0,
                rentBill));
        }

        return rows.OrderBy(r => NaturalStallSortKey(r.StallNo), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Shared delinquency source (dashboard + Financial Reports): occupied stalls behind on payments
    /// over the rolling 12-month window ending at, and EXCLUDING, the given month. Counts unpaid/partial
    /// billing months and sums their balance due (cumulative). Optionally scoped to one facility.
    /// </summary>
    public async Task<IReadOnlyList<DelinquentStallDto>> GetDelinquentStallsAsync(
        FacilityCode? facility, int year, int month, CancellationToken ct)
    {
        var facilities = await _context.Facilities
            .AsNoTracking()
            .Select(f => new { f.Id, f.Code })
            .ToListAsync(ct);
        var codeById = facilities.ToDictionary(f => f.Id, f => f.Code);

        var stallQuery = _context.Stalls
            .AsNoTracking()
            .Where(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive));

        if (facility.HasValue)
        {
            var fid = facilities.FirstOrDefault(f => f.Code == facility.Value)?.Id;
            if (fid is null)
                return Array.Empty<DelinquentStallDto>();
            stallQuery = stallQuery.Where(s => s.FacilityId == fid);
        }

        var stalls = await stallQuery
            .Select(s => new
            {
                s.Id,
                s.StallNo,
                s.FacilityId,
                Occupant = s.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? ""
            })
            .ToListAsync(ct);

        if (stalls.Count == 0)
            return Array.Empty<DelinquentStallDto>();

        var stallIds = stalls.Select(s => s.Id).ToList();
        var since = new DateOnly(year, month, 1).AddMonths(-11);

        // Unpaid/partial billing months in the rolling window, EXCLUDING the current (in-progress) month.
        var unpaidWindow = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId)
                && p.Status != PaymentStatus.Paid
                && (p.BillingYear > since.Year || (p.BillingYear == since.Year && p.BillingMonth >= since.Month))
                && (p.BillingYear < year || (p.BillingYear == year && p.BillingMonth < month)))
            .ToListAsync(ct);

        var stallById = stalls.ToDictionary(s => s.Id);

        return unpaidWindow
            .GroupBy(p => p.StallId)
            .Where(g => stallById.ContainsKey(g.Key))
            .Select(g =>
            {
                var s = stallById[g.Key];
                return new DelinquentStallDto(
                    codeById[s.FacilityId], s.StallNo, s.Occupant, g.Count(), g.Sum(p => p.BalanceDue));
            })
            .OrderByDescending(d => d.MonthsUnpaid)
            .ThenByDescending(d => d.OutstandingBalance)
            .ToList();
    }

    // Orders stall numbers naturally so "2" precedes "10" (and "A2" precedes "A10"): each run of
    // digits is zero-padded to a fixed width before an ordinal compare. Plain string ordering put
    // "10" before "2" across the reports' All Stalls / Status Report tables.
    private static string NaturalStallSortKey(string stallNo) =>
        string.IsNullOrEmpty(stallNo)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(stallNo, "[0-9]+", m => m.Value.PadLeft(12, '0'));

    /// <summary>
    /// Counts months (this year, up to the report month) in which the stall was under an active
    /// contract yet recorded no payment at all — the delinquency signal for the report page.
    /// <para>
    /// Two rules keep this honest:
    /// (1) Months before the contract's effectivity (or after expiry) are never counted — a stall
    ///     cannot be "behind" on a month it was not yet operating.
    /// (2) For NPM, which is collected daily, a month counts as paid if it has either a paid daily
    ///     collection OR a non-Unpaid monthly record. Without this, every NPM stall would read as
    ///     fully delinquent because daily payors rarely have monthly "Paid" records.
    /// Other facilities keep the monthly-billing rule: a month is missed unless it has a fully-Paid
    /// record (Partial still counts as behind, matching the dashboard delinquency definition).
    /// </para>
    /// </summary>
    private static int CountMissedMonths(
        List<PaymentRecord> paymentRecords,
        Stall stall,
        DateOnly endDate,
        bool isNpm,
        HashSet<int>? dailyPaidMonths)
    {
        dailyPaidMonths ??= new HashSet<int>();

        var stallPayments = paymentRecords
            .Where(pr => pr.StallId == stall.Id && pr.BillingYear == endDate.Year)
            .ToList();

        // Months with a fully-Paid record (non-NPM "covered" rule).
        var paidMonths = stallPayments
            .Where(pr => pr.Status == PaymentStatus.Paid)
            .Select(pr => pr.BillingMonth)
            .ToHashSet();

        // Months with any non-Unpaid record (NPM "paid something" rule).
        var settledMonths = stallPayments
            .Where(pr => pr.Status != PaymentStatus.Unpaid)
            .Select(pr => pr.BillingMonth)
            .ToHashSet();

        var missed = 0;
        var today = PhilippineTime.Today;
        for (var m = 1; m <= endDate.Month; m++)
        {
            var monthStart = new DateOnly(endDate.Year, m, 1);
            var monthEnd = new DateOnly(endDate.Year, m, DateTime.DaysInMonth(endDate.Year, m));

            // Count only fully-elapsed PAST months. The current, in-progress month is never "missed"
            // yet (a payor is not in arrears for a month still underway), and future months are not due
            // (e.g. the Yearly view runs to December). Arrears/delinquency count from past months only.
            if (monthEnd >= today)
                continue;

            // Skip months the stall was not under an active contract (pre-effectivity / post-expiry).
            if (CountNpmCollectableDays(stall, monthStart, monthEnd) == 0)
                continue;

            var covered = isNpm
                ? settledMonths.Contains(m) || dailyPaidMonths.Contains(m)
                : paidMonths.Contains(m);

            if (!covered)
                missed++;
        }

        return missed;
    }

    private static string SectionLabel(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Section",
        MarketSection.MeatSection => "Meat Section",
        _ => string.Empty
    };

    #endregion

}
