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

// Partial of StallRepository: the Register of Inactive Stall Accounts (IClosedStallAccountQueries) - accounts closed by an
// admin or whose term lapsed, with what they collected in their lifetime and what they still owe.
//
// Kept apart from current delinquency by design: these are INACTIVE accounts, and a balance on one is not period-bound.
public partial class StallRepository
{
    /// <summary>
    /// Inactive accounts register. CLOSED = Status==Closed (frozen by an admin). EXPIRED = active stall
    /// whose contract term has lapsed (ExpiryDate &lt; today). Lifetime collected counts ALL money ever
    /// received (closure/expiry never erases history). Uncollected = arrears that accrued from contract
    /// effectivity up to the end point (close date for closed, contract expiry for expired), with
    /// excused months / absent days owing nothing — the same billing rules the reports use, contract-
    /// gated (the stall WAS operating then) and bounded to the end point so nothing is back/over-billed.
    /// </summary>
    public async Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsAsync(CancellationToken ct)
        => await GetClosedStallAccountsCoreAsync(null, null, ct);

    /// <summary>
    /// The same register, bounded to a period: every figure states what each ended occupancy owed and paid FOR
    /// <paramref name="from"/>–<paramref name="to"/>, and an occupancy that did not exist in that period is not
    /// listed at all. This is what a year or a month view of the follow-up history must state beside a period
    /// heading; the lifetime reading above answers "what is owed in total" and belongs to the cumulative view.
    /// </summary>
    public async Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsForPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
        => await GetClosedStallAccountsCoreAsync(from, to, ct);

    private async Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsCoreAsync(
        DateOnly? windowStart, DateOnly? windowEnd, CancellationToken ct)
    {
        var today = _clock.PhilippineToday;

        // Resolve the municipality's NPM rates as of today (falls back to the ordinance constants, so
        // Cantilan's lifetime/uncollected figures are unchanged).
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        var npmDailyRate = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, today);
        var npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, today);
        var npmMonthlyRent = rateSnapshot.Resolve(FeeRateKey.NpmMonthlyStall, today);

        // Candidates: every stall that has EVER been let. The register is a record of ended OCCUPANCIES, not of
        // currently-vacant stalls: a stall re-let to a new lessee must still show the previous lessee's closed or
        // expired account, with that lessee's own money — otherwise re-letting a stall erases its history from the
        // office's records. Expiry (= effectivity + duration years) is domain-computed and cannot be translated
        // into SQL, so the stalls are loaded and their occupancies derived in memory. ALL contracts are included,
        // terminated ones too, because a terminated occupancy is exactly what this register is for.
        var everLet = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts)
            .Where(s => s.Contracts.Any())
            .ToListAsync(ct);

        // One entry per ended occupancy: terminated, superseded by a new lessee, lapsed, or frozen by closure.
        // The occupancy in force is not an inactive account.
        var occupanciesByStall = everLet.ToDictionary(s => s.Id, s => s.Occupancies(today));

        var accounts = everLet
            .SelectMany(s => occupanciesByStall[s.Id].Select(o => (Stall: s, Occupancy: o)))
            .Where(x => !x.Occupancy.IsCurrent || x.Stall.Status == StallStatus.Closed)
            // A period-scoped read lists only the occupancies that existed in that period: a 2023 view showing an
            // account that began in 2026 states a debt nobody could have owed then.
            .Where(x => windowStart is not { } ws || windowEnd is not { } we
                || (x.Occupancy.Start <= we && ws <= x.Occupancy.End))
            .ToList();

        if (accounts.Count == 0)
            return new List<ClosedStallAccountDto>();

        var stallIds = accounts.Select(x => x.Stall.Id).Distinct().ToList();

        // Batch-load the financial inputs once (no N+1).
        var payments = await _context.PaymentRecords.AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId)).ToListAsync(ct);
        var paidDailies = await _context.DailyCollections.AsNoTracking()
            .Where(d => stallIds.Contains(d.StallId) && d.IsPaid)
            .Select(d => new { d.StallId, d.CollectionDate, d.DailyFee, d.FishKilos }).ToListAsync(ct);
        var absentDailies = await _context.DailyCollections.AsNoTracking()
            .Where(d => stallIds.Contains(d.StallId) && d.IsAbsent)
            .Select(d => new { d.StallId, d.CollectionDate }).ToListAsync(ct);
        var exceptions = await _context.StallMonthlyExceptions.AsNoTracking()
            .Where(e => stallIds.Contains(e.StallId))
            .Select(e => new { e.StallId, e.BillingYear, e.BillingMonth }).ToListAsync(ct);
        // Days the market itself was shut. Nothing is owed for them, so charging them here would state a debt the
        // office cannot collect — and the Record-payment dialog, which has always excluded them, would then offer a
        // smaller total than this register. One closure list serves every stall: a closure is facility-wide.
        var closureDates = (await _context.NpmMarketClosures.AsNoTracking()
            .Select(c => c.ClosureDate).ToListAsync(ct)).ToHashSet();

        var paidByStall = paidDailies.GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var absentByStall = absentDailies.GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate).ToHashSet());
        var paymentsByStall = payments.GroupBy(p => p.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var excusedByStall = exceptions.GroupBy(e => e.StallId).ToDictionary(g => g.Key, g => g.Select(x => (x.BillingYear, x.BillingMonth)).ToHashSet());

        var result = new List<ClosedStallAccountDto>(accounts.Count);
        foreach (var (stall, occupancy) in accounts)
        {
            var contract = occupancy.Contract;
            var isNpm = stall.Facility?.Code == FacilityCode.NPM;
            var isClosed = stall.Status == StallStatus.Closed;

            var contractExpiry = contract.ExpiryDate;
            // End point of THIS occupancy: the day the lessee actually stopped holding the stall — terminated,
            // superseded by the next lessee, frozen by closure, or the term's end. Bounding every figure below to
            // [occupancy start, end] is what keeps one lessee's money out of another's account on a re-let stall.
            var startDate = occupancy.Start;
            var endDate = occupancy.End;
            // Charges stop at the term's end even if the lessee stayed on afterwards.
            var billableEnd = occupancy.BillableEnd;

            // A period-scoped read narrows all three to the requested window, so every figure on the row is what
            // this occupancy owed and paid FOR that period — never its lifetime total under a period heading.
            if (windowStart is { } wStart && windowEnd is { } wEnd)
            {
                if (startDate < wStart) startDate = wStart;
                if (endDate > wEnd) endDate = wEnd;
                if (billableEnd > wEnd) billableEnd = wEnd;
            }

            var windows = occupanciesByStall[stall.Id];

            // A month's charge is one indivisible obligation, so exactly one occupancy answers for it (the lessee
            // who began latest within it). Without this a mid-month handover billed that month in full to BOTH
            // lessees and credited its payment to both — the register's totals then overstated by a month's rent
            // per handover.
            bool AnswersFor(int billingYear, int billingMonth)
            {
                if (billingMonth is < 1 or > 12) return false;

                var monthStart = new DateOnly(billingYear, billingMonth, 1);
                var monthEnd = new DateOnly(billingYear, billingMonth, DateTime.DaysInMonth(billingYear, billingMonth));

                // Inside the read's own window (a period-scoped read states only that period's months) …
                if (windowStart is { } ws && windowEnd is { } we && (monthEnd < ws || we < monthStart))
                    return false;

                // … and answered for by THIS occupancy, judged on the true occupancy windows rather than the
                // clamped ones, so narrowing the view never moves a month to a different lessee.
                return StallOccupancy.AnsweringForMonth(windows, billingYear, billingMonth)?.Contract.Id == contract.Id;
            }

            var stallPaid = (paidByStall.GetValueOrDefault(stall.Id) ?? new())
                // Daily collections carry the business date they were collected FOR, so they attribute exactly.
                .Where(d => d.CollectionDate >= startDate && d.CollectionDate <= endDate)
                .ToList();
            var stallAbsent = absentByStall.GetValueOrDefault(stall.Id) ?? new();
            var stallPayments = (paymentsByStall.GetValueOrDefault(stall.Id) ?? new())
                // Attributed by the BILLING period, never by the day the money arrived: an arrear settled months
                // after a handover still belongs to the lessee who incurred it.
                .Where(p => AnswersFor(p.BillingYear, p.BillingMonth))
                .ToList();
            var stallExcused = excusedByStall.GetValueOrDefault(stall.Id) ?? new();

            // Lifetime collected = every peso actually received (status-independent). A period-scoped read states
            // what was received FOR that period.
            var lifetimeCollected = isNpm
                ? stallPaid.Sum(d => d.DailyFee + (d.FishKilos.HasValue ? d.FishKilos.Value * npmFishRate : 0m))
                : stallPayments.Sum(p => p.AmountPaid);

            // The rent this occupancy was let at. The stall's own MonthlyRate is the CURRENT figure and is rewritten
            // when the space is re-let or its rate revised, so reading it here would restate a departed lessee's
            // arrears at a rate they never agreed to. Legacy terms that carry no rate fall back to the stall's.
            var occupancyMonthlyRate = contract.MonthlyRentalRate > 0m ? contract.MonthlyRentalRate : stall.MonthlyRate;

            decimal uncollected = 0m;
            if (isNpm)
            {
                // Per calendar month, from the monthly obligation ledger: the month's contractual rent (₱900 for a
                // month held in full, whatever the calendar gave it), less the days nothing is owed for, less what
                // was actually collected. The ₱30 fee is the installment, not the measure — so a lapsed account's
                // arrears read as months of rent and a complete year as twelve of them.
                var cursor = new DateOnly(startDate.Year, startDate.Month, 1);
                var lastMonth = new DateOnly(billableEnd.Year, billableEnd.Month, 1);
                while (cursor <= lastMonth)
                {
                    var mStart = cursor > startDate ? cursor : startDate;
                    var daysInMonth = DateTime.DaysInMonth(cursor.Year, cursor.Month);
                    var mEnd = new DateOnly(cursor.Year, cursor.Month, daysInMonth);
                    if (mEnd > billableEnd) mEnd = billableEnd;
                    // And never past today. An occupancy's billable end can sit in the future — a stall frozen with no
                    // recorded closure date keeps its term's expiry — so without this the register billed whole
                    // unearned months and stated a larger balance than the stall profile for the same account. This
                    // is the sixth path of the earned-obligation rule; the other five already applied it.
                    mEnd = DomainRules.EarnedThrough(mEnd, today);
                    if (mEnd < mStart)
                    {
                        cursor = cursor.AddMonths(1);
                        continue;
                    }

                    var daysHeld = mEnd.DayNumber - mStart.DayNumber + 1;
                    var daysForgiven = 0;
                    for (var d = mStart; d <= mEnd; d = d.AddDays(1))
                    {
                        if (stallAbsent.Contains(d) || closureDates.Contains(d)) daysForgiven++;
                    }

                    // The rate in force at the end of the counted span — the rent it is measured against is that
                    // month's.
                    var monthFee = NpmDailyFee.ForStall(stall, rateSnapshot, mEnd);
                    var obligation = DomainRules.DailyBilledMonthObligation(
                        monthFee,
                        stall.ResolveMonthlyRent(
                            NpmDailyFee.ForStall(stall, rateSnapshot, mEnd),
                            rateSnapshot.Resolve(FeeRateKey.NpmMonthlyStall, mEnd)),
                        daysInMonth,
                        daysHeld);
                    var credit = DomainRules.DailyBilledMonthCredit(monthFee, obligation, daysHeld, daysForgiven);
                    var collected = stallPaid
                        .Where(p => p.CollectionDate >= mStart && p.CollectionDate <= mEnd)
                        .Sum(p => p.DailyFee);

                    uncollected += DomainRules.DailyBilledMonthOutstanding(obligation, collected, credit);
                    cursor = cursor.AddMonths(1);
                }
            }
            else
            {
                // Per calendar month this occupancy answered for: a non-Unpaid record's balance, else the full
                // monthly rent. Excused months owe nothing. The walk stops at the term's own last billing month —
                // a term of N years owes N × 12 months, so it must not spill into the anniversary month, which is
                // how a three-year term came to read thirty-seven months.
                var cursor = new DateOnly(startDate.Year, startDate.Month, 1);
                var endMonth = new DateOnly(billableEnd.Year, billableEnd.Month, 1);
                while (cursor <= endMonth)
                {
                    if (!stallExcused.Contains((cursor.Year, cursor.Month))
                        && AnswersFor(cursor.Year, cursor.Month)
                        && contract.BillsCalendarMonth(cursor.Year, cursor.Month))
                    {
                        var rec = stallPayments.FirstOrDefault(p => p.BillingYear == cursor.Year && p.BillingMonth == cursor.Month);
                        uncollected += rec is not null && rec.Status != PaymentStatus.Unpaid
                            ? rec.BalanceDue
                            : occupancyMonthlyRate;
                    }
                    cursor = cursor.AddMonths(1);
                }
            }

            // Three kinds of inactive account, because they are not the same thing to a collector:
            //   Closed    — the stall was frozen by a head/admin. Nothing is owed going forward.
            //   Superseded — the space was handed to the next lessee (or the contract terminated on a stated date).
            //                That account is finished; its balance is this register's to state.
            //   Lapsed    — the term ran out but the space was never handed over and the stall is still open, which
            //                in practice means the tenant is still trading there. The office keeps collecting from
            //                them, so the account also remains in the arrears and follow-up lists. Saying it was
            //                "excluded from current billing and delinquency" was untrue for 57 of Cantilan's 58.
            //
            // A fourth case: the SAME lessee took a fresh term. Stall 23 was Vincent E. Doloriel renewing his own
            // space, and reading "Handed over" beside a stall he is still trading in — with an "Assign new stall"
            // button — told the office both that he had gone and that the space was free to offer.
            var later = occupanciesByStall[stall.Id]
                .Where(o => o.Start > occupancy.Start)
                .OrderBy(o => o.Start)
                .FirstOrDefault();
            var sameLessee = later is not null
                && string.Equals(
                    (later.Contract.ActualOccupant ?? string.Empty).Trim(),
                    (contract.ActualOccupant ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);

            var state = isClosed
                ? InactiveAccountState.Closed
                : sameLessee ? InactiveAccountState.Renewed
                : later is not null || occupancy.Contract.EndedOn is not null ? InactiveAccountState.Superseded
                : InactiveAccountState.Lapsed;

            result.Add(new ClosedStallAccountDto(
                stall.Id,
                state,
                stall.Facility!.Code,
                stall.Facility!.Name,
                stall.StallNo,
                // "Closed" is a status, not a person. Three imported contracts carry it as the occupant while the
                // real lessee's name sits on the contract line, and this register printed the status word where a
                // name belongs. What is stored is left alone so the import stays auditable.
                OccupantName.Resolve(contract.ActualOccupant, contract.NameOnContract),
                contract.NameOnContract,
                contract.EffectivityDate,
                contract.DurationYears,
                // A daily-collected stall has no monthly contract rate: state the rent the space is let for — the
                // LGU's own stated market month, or thirty of its daily fee, and a custom section's own rate for its
                // own month — never the hand-entered figure stored on the stall. A monthly facility states the rent
                // THIS occupancy was let at, which is also the figure the collection dialog offers.
                isNpm ? stall.ResolveMonthlyRent(NpmDailyFee.ForStall(stall, rateSnapshot, today), npmMonthlyRent) : occupancyMonthlyRate,
                isClosed ? stall.ClosedAt : null,
                contractExpiry,
                lifetimeCollected,
                uncollected,
                stall.UpdatedBy,
                // The tenant's own section label (canonical sections) or the stall's custom section name, so
                // the register can be filtered and printed section by section like the roster.
                stall.Section is { } closedSec
                    ? (stall.Facility!.SectionLabel(closedSec) ?? GetSectionName(closedSec))
                    : (stall.CustomSectionName ?? string.Empty),
                // The day this lessee actually stopped holding the stall. Differs from the term's expiry when the
                // occupancy ended early — handed to the next lessee, or frozen by closure — and it is the date the
                // register must show, otherwise a handover looks like a contract still running. Stated as the fact
                // it is, even on a period-scoped read whose FIGURES stop at the end of the period.
                occupancy.End,
                // Somebody else holds this stall now, so this row is history only: the register must not offer to
                // renew or reopen it, which would act on the sitting lessee's occupancy.
                //
                // "Somebody ELSE" is the point, so this row's own occupancy is excluded. A term of zero years — a space
                // let without a contract — expires on the day it takes effect, so a space let and closed on the same
                // day left its own window still in force. The register concluded the stall had been re-let, told the
                // office the space was taken by the very lessee it had just closed, and offered "Assign new stall" as
                // the only action: no way to resume the account, and no way to remove it.
                stall.Occupancies(today).Any(o => o.IsCurrent && o.Contract.Id != contract.Id),
                // The term this row is the record of, so an action on THIS lessee cannot pick up the sitting one's.
                contract.Id,
                // The space as measured on the stall today — what a renewal is checked against.
                stall.AreaSqm,
                stall.AreaNote));
        }

        return result
            .OrderByDescending(r => r.ClosedOn ?? r.ExpiryDate)
            .ThenBy(r => r.FacilityName)
            .ToList();
    }
}
