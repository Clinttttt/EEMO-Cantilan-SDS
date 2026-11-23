using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public partial class FacilityReportsRepository
{
    /// <summary>
    /// The Statement of Account of every account in one facility, as of today, in one pass: the same arrears walk and month walk
    /// <see cref="GetStallStatementAsync"/> runs for one space (run here for the whole facility), and the same utility and penalty rules,
    /// batched. Null when the facility is not this tenant's.
    /// </summary>
    public async Task<BillingRegisterDto?> GetBillingRegisterAsync(FacilityCode code, CancellationToken ct)
    {
        var facilityId = await _context.Facilities.AsNoTracking().Where(f => f.Code == code).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
        if (facilityId is not { } id) return null;

        var today = _clock.PhilippineToday;
        var behind = (await AccountsBehindAsync(code, null, today.Year, today.Month, wholeAccount: true, ct))
            .Where(b => b.Account.StallId.HasValue)
            .GroupBy(b => b.Account.StallId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Account);

        var (monthStart, monthEnd) = CalculateDateRange(ReportPeriod.Monthly, today.Year, today.Month, null);
        await LoadNpmRatesAsync(monthEnd, ct);
        var month = (await GenerateStallComplianceAsync(code, id, monthStart, monthEnd, ct)).ToDictionary(r => r.StallId);
        var governing = BillingPolicyVersion.InForce(await _context.BillingPolicyVersions.AsNoTracking().ToListAsync(ct), today);
        var rules = governing?.Rules ?? BillingPolicy.StallTrackDefaults;

        var stalls = await _context.Stalls.AsNoTracking().Include(s => s.Contracts).Where(s => s.FacilityId == id).ToListAsync(ct);
        var utilities = await UtilityBalancesAsync(stalls, today, ct);
        var penalties = (await OwedFinesAsync(stalls, today, ct)).ToLookup(f => f.StallId);
        var ahead = IsDailyMarket(code) ? new Dictionary<Guid, PaidAheadSum>() : await PaidAheadAsync(month.Keys.Union(behind.Keys).ToList(), today, ct);

        var rows = month.Keys.Union(behind.Keys)
            .Select(stallId =>
            {
                month.TryGetValue(stallId, out var m);
                behind.TryGetValue(stallId, out var b);
                var paidAhead = ahead.GetValueOrDefault(stallId);
                return new BillingRegisterRowDto(
                    stallId,
                    m?.StallNo ?? b!.StallNo,
                    m?.Occupant ?? b!.Occupant,
                    m?.Section ?? b!.Section,
                    (b?.ArrearsMonths ?? 0) + (b?.DelinquentMonths ?? 0),
                    (b?.ArrearsAmount ?? 0m) + (b?.DelinquentAmount ?? 0m),
                    m?.AccountAssessment ?? 0m,
                    m?.AmountPaid ?? 0m,
                    m?.ORNumber,
                    m?.Balance ?? 0m,
                    utilities.GetValueOrDefault(stallId),
                    penalties[stallId].Sum(f => f.Owed),
                    penalties[stallId].Where(f => f.IsSurcharge).Sum(f => f.Owed),
                    penalties[stallId].Where(f => f.IsInterest).Sum(f => f.Owed),
                    paidAhead?.Months ?? 0,
                    paidAhead?.Amount ?? 0m,
                    paidAhead?.Through);
            })
            .OrderBy(r => r.Section, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StallNo.Length).ThenBy(r => r.StallNo, StringComparer.OrdinalIgnoreCase)   // D-2 before D-10
            .ToList();

        return new BillingRegisterDto(code, today, IsDailyMarket(code) ? null : rules.DueDate(today.Year, today.Month), rows,
            rules.PenaltyBasis == LatePenaltyBasis.PercentOfUnpaid ? rules.PenaltyValue : null, rules.InterestPercent, rules.InterestMaxMonths,
            rules.ChargesPenalty ? governing?.GoverningReference : null);
    }

    private sealed record PaidAheadSum(int Months, decimal Amount, DateOnly Through);

    /// <summary>
    /// Rent already taken for months after this one (paid ahead, IA-071): the month's own record settles it when it comes, so the money is carried to
    /// that bill. Read from the same payment records the month walk reads, rent only (a month not yet begun has no meter bill).
    /// </summary>
    private async Task<Dictionary<Guid, PaidAheadSum>> PaidAheadAsync(IReadOnlyCollection<Guid> stallIds, DateOnly today, CancellationToken ct)
    {
        var paid = await _context.PaymentRecords.AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId)
                && (pr.BillingYear > today.Year || pr.BillingYear == today.Year && pr.BillingMonth > today.Month)
                && (pr.Status == PaymentStatus.Paid || pr.Status == PaymentStatus.Partial && pr.PartialAmount > 0m))
            .Select(pr => new { pr.StallId, pr.BillingYear, pr.BillingMonth, Amount = pr.Status == PaymentStatus.Paid ? pr.BaseRentalAmount : pr.PartialAmount })
            .ToListAsync(ct);
        return paid.GroupBy(p => p.StallId).ToDictionary(g => g.Key, g => new PaidAheadSum(
            g.Select(p => (p.BillingYear, p.BillingMonth)).Distinct().Count(),
            g.Sum(p => p.Amount),
            g.Max(p => new DateOnly(p.BillingYear, p.BillingMonth, 1))));
    }

    /// <summary><see cref="UnpaidUtilitiesAsync"/> for many spaces at once: each space's metered bills from where its present account began.</summary>
    private async Task<Dictionary<Guid, decimal>> UtilityBalancesAsync(IReadOnlyList<Stall> stalls, DateOnly today, CancellationToken ct)
    {
        var starts = stalls.Select(s => (s.Id, Start: NewestOccupancyStart(s, today))).Where(x => x.Start.HasValue).ToDictionary(x => x.Id, x => x.Start!.Value);
        if (starts.Count == 0) return [];
        var ids = starts.Keys.ToArray();
        var bills = await _context.UtilityBills.AsNoTracking().Where(b => ids.Contains(b.StallId)).ToListAsync(ct);
        return bills
            .Where(b => starts[b.StallId] is var start && (b.BillingYear > start.Year || (b.BillingYear == start.Year && b.BillingMonth >= start.Month)))
            .GroupBy(b => b.StallId)
            .ToDictionary(g => g.Key, g => g.Sum(b => Math.Max(0m, b.ElecBalanceDue) + Math.Max(0m, b.WaterBalanceDue)));
    }

    private sealed record OwedFine(Guid StallId, string Code, DateOnly? Period, decimal Owed)
    {
        public bool IsSurcharge => Code == FineAssessment.SurchargeCode;
        public bool IsInterest => Code == FineAssessment.InterestCode;
    }

    /// <summary>Fines and late charges still owed on each space's present account, less what was collected on them. An earlier occupant's are that account's.</summary>
    private async Task<List<OwedFine>> OwedFinesAsync(IEnumerable<Stall> stalls, DateOnly today, CancellationToken ct)
    {
        var accounts = stalls.Select(s => (s.Id, Contract: s.Occupancies(today).LastOrDefault()?.Contract.Id)).Where(x => x.Contract.HasValue)
            .ToDictionary(x => x.Contract!.Value, x => x.Id);
        if (accounts.Count == 0) return [];
        var contractIds = accounts.Keys.ToArray();
        var fines = await _context.FineAssessments.AsNoTracking().Where(a => a.ContractId != null && contractIds.Contains(a.ContractId.Value))
            .Select(a => new { a.Id, a.MunicipalityId, a.ContractId, a.PenaltyCode, a.RelatedPeriodStart, Due = a.AdjustedAmount ?? a.AssessedAmount }).ToListAsync(ct);
        if (fines.Count == 0) return [];
        var settled = await new EEMOCantilanSDS.Application.Common.Revenue.FineAssessmentCollectionSource(_context)
            .SettledAsync(fines[0].MunicipalityId, fines.Select(f => f.Id).ToArray(), ct);
        return fines.Select(f => new OwedFine(accounts[f.ContractId!.Value], f.PenaltyCode, f.RelatedPeriodStart, Math.Max(0m, f.Due - settled.GetValueOrDefault(f.Id))))
            .Where(f => f.Owed > 0m).ToList();
    }

    /// <summary>Each late rent month's surcharge and interest still owed, oldest first.</summary>
    private static IReadOnlyList<LateChargeMonthDto> LateMonths(IEnumerable<OwedFine> owed) => owed
        .Where(f => (f.IsSurcharge || f.IsInterest) && f.Period.HasValue)
        .GroupBy(f => f.Period!.Value).OrderBy(g => g.Key)
        .Select(g => new LateChargeMonthDto(g.Key.Year, g.Key.Month, g.Where(f => f.IsSurcharge).Sum(f => f.Owed), g.Where(f => f.IsInterest).Sum(f => f.Owed)))
        .ToList();
}
