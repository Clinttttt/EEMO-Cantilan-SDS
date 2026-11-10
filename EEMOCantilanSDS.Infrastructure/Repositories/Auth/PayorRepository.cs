using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class PayorRepository(
    AppDbContext context,
    INpmMonthSettlementService npmMonthSettlementService,
    IFeeRateResolver feeRateResolver,
    IClock clock,
    IFacilityReportsRepository reports) : IPayorRepository
{
    /// <summary>
    /// Test/non-DI convenience, matching the other repositories: the real clock, and the office's own rate rows read from the
    /// same context, so a daily fee resolves exactly as it does under dependency injection.
    /// </summary>
    public PayorRepository(AppDbContext context, INpmMonthSettlementService npmMonthSettlementService)
        : this(context, npmMonthSettlementService, new Fees.FeeRateResolver(context), new SystemClock(), new FacilityReportsRepository(context)) { }
    public async Task<PayorUser?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default)
    {
        var normalized = contactNumber.Trim();
        // Login derives the tenant from the user, so span every LGU (bypass the tenant filter) while still
        // excluding soft-deleted accounts. Subdomain-scoped login is the Phase-5 refinement.
        return await context.PayorUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Username == normalized, ct);
    }

    public async Task<PayorUser?> GetPayorByIdAsync(Guid payorUserId, CancellationToken ct = default)
    {
        // The id is the caller's own, taken from the token, so no other payor is reachable from here. Soft-deleted rows
        // stay excluded by the global filter.
        return await context.PayorUsers.FirstOrDefaultAsync(p => p.Id == payorUserId, ct);
    }

    public async Task<PayorActivationCode?> GetActivationCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim();
        // Activation is anonymous (no JWT → resolves to the DEFAULT tenant), and the code is a globally
        // unique, single-use secret. Span every LGU (bypass the tenant filter) so a code issued by ANY
        // municipality is found; still exclude soft-deleted. The handler then pins the request to the
        // code's own municipality before creating the payor.
        return await context.PayorActivationCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Code == normalized, ct);
    }

    /// <summary>
    /// The name the office holds for the stall's occupant, pinned to the code's own municipality.
    ///
    /// <para>
    /// Read across the tenant filter and constrained to <paramref name="municipalityId"/> in the query itself: activation is
    /// anonymous, so no session has pinned an LGU at this point, and an ambient default must not decide whose name is read.
    /// Stating the municipality makes it impossible for one LGU's occupant name to answer another LGU's activation.
    /// </para>
    /// </summary>
    public async Task<string?> GetOccupantNameAsync(Guid stallId, Guid municipalityId, CancellationToken ct = default)
    {
        var name = await context.Contracts
            .IgnoreQueryFilters()
            .Where(c => c.StallId == stallId
                        && c.MunicipalityId == municipalityId
                        && !c.IsDeleted
                        && c.IsActive
                        && c.ActualOccupant != "")
            .OrderByDescending(c => c.EffectivityDate)
            .Select(c => c.ActualOccupant)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public async Task<bool> ActivationCodeExistsAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim();
        return await context.PayorActivationCodes
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Code == normalized, ct);
    }

    public async Task<bool> ActiveCodeExistsForContactOnOtherStallAsync(string contactNumber, Guid stallId, CancellationToken ct = default)
    {
        var normalized = contactNumber.Trim();
        var now = DateTime.UtcNow;
        return await context.PayorActivationCodes
            .AnyAsync(c => c.ContactNumber == normalized
                && c.StallId != stallId
                && !c.IsUsed
                && c.ExpiresAt > now, ct);
    }

    public async Task RemoveCodesForStallAsync(Guid stallId, CancellationToken ct = default)
    {
        // Hard-delete every prior code for the stall (IgnoreQueryFilters catches any soft-deleted
        // remnants too) so issuing a new one leaves exactly one record per stall.
        var existing = await context.PayorActivationCodes
            .IgnoreQueryFilters()
            .Where(c => c.StallId == stallId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            context.PayorActivationCodes.RemoveRange(existing);
    }

    public async Task AddActivationCodeAsync(PayorActivationCode code, CancellationToken ct = default)
    {
        await context.PayorActivationCodes.AddAsync(code, ct);
    }

    public async Task<bool> LinkExistsAsync(Guid payorUserId, Guid stallId, CancellationToken ct = default)
    {
        return await context.PayorStallLinks
            .AnyAsync(l => l.PayorUserId == payorUserId && l.StallId == stallId, ct);
    }

    public async Task AddPayorAsync(PayorUser payor, CancellationToken ct = default)
    {
        await context.PayorUsers.AddAsync(payor, ct);
    }

    public async Task AddStallLinkAsync(PayorStallLink link, CancellationToken ct = default)
    {
        await context.PayorStallLinks.AddAsync(link, ct);
    }

    public async Task<int> RemoveStallLinksAsync(Guid stallId, CancellationToken ct = default)
    {
        // Tenant-scoped by design: the link is municipality-owned and this runs inside the stall's tenant,
        // so the query filter already confines removal to the current LGU's links for the stall.
        var links = await context.PayorStallLinks
            .Where(l => l.StallId == stallId)
            .ToListAsync(ct);

        if (links.Count == 0)
            return 0;

        context.PayorStallLinks.RemoveRange(links);
        return links.Count;
    }

    public async Task<IReadOnlyList<PayorStallBalanceDto>> GetBalancesAsync(Guid payorUserId, CancellationToken ct = default)
    {
        var stalls = await GetLinkedStallsAsync(payorUserId, ct);
        if (stalls.Count == 0)
            return Array.Empty<PayorStallBalanceDto>();

        var items = await BuildPayableItemsAsync(stalls, ct);
        var byStall = items.Where(i => !i.Advance).GroupBy(i => i.StallId).ToDictionary(g => g.Key, g => g.ToList());

        // The office's wall clock decides which month is current, and its own rates decide a day's fee. The snapshot is read
        // at most once, and only where a daily-billed stall is actually present.
        var today = DateOnly.FromDateTime(clock.PhilippineNow);
        FeeRateSnapshot? snapshot = null;

        var result = new List<PayorStallBalanceDto>();
        foreach (var stall in stalls)
        {
            byStall.TryGetValue(stall.Id, out var stallItems);
            stallItems ??= new List<PayorPayableItemDto>();
            var oldest = stallItems.OrderBy(i => i.Year).ThenBy(i => i.Month).FirstOrDefault();
            var occupant = stall.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant ?? "—";

            // A market stall is charged by the day, so that is what its own portal must say. It was being shown the stall's
            // monthly rate, a figure the payor is never billed, beside a balance built from days: the two could not be
            // reconciled by the person paying. The days owed come from the same settlement service the payable item and the
            // collector's app use, and the day's fee from the same rule, so the screen cannot disagree with the charge.
            var isDaily = stall.Facility!.Code == FacilityCode.NPM && !stall.Facility.IsMonthlyRentMarket;   // a market rented by the month shows its monthly rent
            var dailyRate = 0m;
            var daysOwed = 0;
            var balance = stallItems.Sum(i => i.BalanceDue);

            if (isDaily)
            {
                var payable = await npmMonthSettlementService.ComputePayableAsync(stall, today.Year, today.Month, ct);
                daysOwed = payable.Days;
                dailyRate = NpmDailyFee.ForStall(stall, snapshot ??= await feeRateResolver.GetSnapshotAsync(ct), today);

                // The fish section's payable item deliberately carries NO amount: each of its days costs the base fee plus
                // that day's weighing fee, which only the payor can declare, so the days are offered one by one instead of
                // billed as one figure for the month. Summing the items therefore reported nothing owed. The days are owed
                // all the same, and the base fee for them is certain, so that is this space's balance; the weighing fee is
                // added to a day as it is declared. Without this a fish stall read ₱0.00 beside "2 days owed this month"
                // while the office's own stall profile read ₱60, and the payor could not tell which was true. The figure is
                // the settlement service's own, the same one the office's ledger and the collector's app settle against, so
                // the two screens cannot disagree. Non-fish sections are untouched: their month item already carries it.
                if (stall.Section == MarketSection.FishSection)
                    balance += payable.Amount;
            }

            result.Add(new PayorStallBalanceDto(
                stall.Id,
                stall.StallNo,
                stall.Facility!.Code,
                occupant,
                stall.MonthlyRentFor(today.Year, today.Month, today),
                balance,
                stallItems.Count,
                oldest?.Period,
                isDaily,
                dailyRate,
                daysOwed,
                stall.Facility.Name,
                string.IsNullOrWhiteSpace(stall.Facility.ShortName) ? null : stall.Facility.ShortName));
        }

        return result.OrderByDescending(r => r.OutstandingBalance).ToList();
    }

    public async Task<IReadOnlyList<PayorPayableItemDto>> GetPayableItemsAsync(Guid payorUserId, CancellationToken ct = default)
    {
        var stalls = await GetLinkedStallsAsync(payorUserId, ct);
        if (stalls.Count == 0)
            return Array.Empty<PayorPayableItemDto>();

        var items = await BuildPayableItemsAsync(stalls, ct);
        return items.OrderBy(i => i.Year).ThenBy(i => i.Month).ToList();
    }

    private async Task<List<Stall>> GetLinkedStallsAsync(Guid payorUserId, CancellationToken ct)
    {
        var stallIds = await context.PayorStallLinks
            .Where(l => l.PayorUserId == payorUserId)
            .Select(l => l.StallId)
            .ToListAsync(ct);

        if (stallIds.Count == 0)
            return new List<Stall>();

        return await context.Stalls
            .Where(s => stallIds.Contains(s.Id))
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Builds the payable obligations for the given stalls: every existing unpaid/partial record, every elapsed
    /// month the office counts as owed but holds no record for, PLUS a synthesized current-month charge for
    /// monthly-rental stalls that are active, under an effective contract, and have no record yet for the current
    /// month. NPM (daily-billed) is excluded from that synthesis — its obligation is day-based, handled elsewhere.
    /// </summary>
    // shortcut: months are added to the records, not replaced by the office's walk, so a record the walk leaves out (an earlier occupancy's, an excused month's) still shows here. Replace once those records have their own home on the portal; hiding them today would hide real debt.
    private async Task<List<PayorPayableItemDto>> BuildPayableItemsAsync(List<Stall> stalls, CancellationToken ct)
    {
        var stallIds = stalls.Select(s => s.Id).ToList();

        // BalanceDue/PeriodKey are computed (unmapped) — materialize, then work in memory.
        var nonPaid = await context.PaymentRecords
            .Where(p => stallIds.Contains(p.StallId) && p.Status != PaymentStatus.Paid)
            .ToListAsync(ct);

        var nonPaidIds = nonPaid.Select(r => r.Id).ToList();
        var underReview = (await context.OnlinePaymentTransactions
                .Where(t => t.PaymentRecordId != null && nonPaidIds.Contains(t.PaymentRecordId.Value)
                    && t.Status == OnlinePaymentStatus.ReconciliationRequired && t.Disposition == null)
                .Select(t => t.PaymentRecordId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var today = clock.PhilippineToday;
        int curYear = today.Year, curMonth = today.Month;

        // Stalls that already have ANY record for the current month (paid or not) — don't synthesize for these.
        var hasCurrentMonthRecord = (await context.PaymentRecords
                .Where(p => stallIds.Contains(p.StallId) && p.BillingYear == curYear && p.BillingMonth == curMonth)
                .Select(p => p.StallId)
                .ToListAsync(ct))
            .ToHashSet();

        var monthStart = new DateOnly(curYear, curMonth, 1);
        var monthEnd = new DateOnly(curYear, curMonth, DateTime.DaysInMonth(curYear, curMonth));

        var rules = BillingPolicyVersion.InForce(await context.BillingPolicyVersions.AsNoTracking().ToListAsync(ct), today)?.Rules ?? BillingPolicy.StallTrackDefaults;
        var recordedAhead = (await context.PaymentRecords
                .Where(p => stallIds.Contains(p.StallId) && (p.BillingYear > curYear || (p.BillingYear == curYear && p.BillingMonth > curMonth)))
                .Select(p => new { p.StallId, p.BillingYear, p.BillingMonth })
                .ToListAsync(ct))
            .Select(p => (p.StallId, p.BillingYear, p.BillingMonth))
            .ToHashSet();

        var items = new List<PayorPayableItemDto>();
        foreach (var stall in stalls)
        {
            var facility = stall.Facility!.Code;

            // 1) Existing arrears / partials (real records with a remaining balance).
            foreach (var r in nonPaid.Where(r => r.StallId == stall.Id && r.BalanceDue > 0m))
            {
                items.Add(new PayorPayableItemDto(
                    stall.Id, stall.StallNo, facility, r.BillingYear, r.BillingMonth, r.PeriodKey, r.BalanceDue)
                {
                    // Initiate pauses a source that is transitioning (PendingCutover) or has a capture under review; Legacy and Canonical rows are otherwise payable.
                    PayableOnline = r.SettlementAuthorityState != SettlementAuthority.PendingCutover && !underReview.Contains(r.Id),
                    PaymentUnderReview = underReview.Contains(r.Id)
                });
            }

            // Nothing writes a record until money moves, so a month nobody paid had no row here while the office counted it as owed: two totals for one account.
            var isMonthly = facility != FacilityCode.NPM || stall.Facility.IsMonthlyRentMarket;
            var statement = await reports.GetStallStatementAsync(stall.Id, ct);
            foreach (var missed in statement?.UnpaidMonths ?? [])
            {
                if (nonPaid.Any(r => r.StallId == stall.Id && r.BillingYear == missed.Year && r.BillingMonth == missed.Month))
                    continue;

                // A month with no record from before these rules begin says nothing about whether it was paid, so it is not asked of a payor (IA-075).
                if (!rules.Counts(missed.Year, missed.Month))
                    continue;

                // Monthly rent states what Initiate will charge for the month, so the row and the checkout cannot differ.
                var owed = isMonthly ? stall.MonthlyRentFor(missed.Year, missed.Month, today) : missed.Unpaid;
                if (owed > 0m)
                    items.Add(new PayorPayableItemDto(
                        stall.Id, stall.StallNo, facility, missed.Year, missed.Month, $"{missed.Year:0000}-{missed.Month:00}", owed,
                        isMonthly ? PayorPayableKind.Monthly : PayorPayableKind.NpmDaily));
            }

            // Electricity and water: each bill still owed, whatever its month, the same figure the office's statement states.
            foreach (var bill in statement?.UnpaidUtilities ?? [])
            {
                items.Add(new PayorPayableItemDto(
                    stall.Id, stall.StallNo, facility, bill.Year, bill.Month, $"{bill.Year:0000}-{bill.Month:00}", bill.Unpaid, PayorPayableKind.NpmUtility));
            }

            if (statement is { PenaltiesDue: > 0m })
                items.Add(new PayorPayableItemDto(
                    stall.Id, stall.StallNo, facility, curYear, curMonth, $"{curYear:0000}-{curMonth:00}", statement.PenaltiesDue, PayorPayableKind.Penalty));

            // 2) Synthesized current-month obligation for monthly-rental facilities.
            var dueThisMonth = isMonthly
                && stall.MonthlyRentFor(curYear, curMonth, today) > 0m
                && stall.Status == StallStatus.Active
                && !hasCurrentMonthRecord.Contains(stall.Id)
                && stall.Contracts.Any(c => c.IsActive && c.EffectivityDate <= monthEnd && monthStart <= c.ExpiryDate);

            if (dueThisMonth)
            {
                items.Add(new PayorPayableItemDto(
                    stall.Id, stall.StallNo, facility, curYear, curMonth,
                    $"{curYear:0000}-{curMonth:00}", stall.MonthlyRentFor(curYear, curMonth, today)));
            }

            // 3) NPM (daily-billed): synthesize the current-month base-fee balance — ₱30 × the month's
            // unpaid, elapsed, in-term, non-closed days — from the shared settlement service, so the amount
            // shown equals what initiate charges and settlement marks. Fish ₱/kg and utilities are excluded
            // (weighed/metered at the stall). Only shown when there is an outstanding daily balance.
            if (facility == FacilityCode.NPM && !stall.Facility.IsMonthlyRentMarket
                && stall.Status == StallStatus.Active
                && stall.Contracts.Any(c => c.IsActive && c.EffectivityDate <= monthEnd && monthStart <= c.ExpiryDate))
            {
                if (stall.Section == MarketSection.FishSection)
                {
                    // Fish section: per-DAY self-declare (base + kilos × fish rate). The base can't be
                    // pre-bulked because each day's total depends on that day's kilos — so instead of the
                    // base-only month item, offer the uncollected days for the payor to declare + pay one.
                    var days = await npmMonthSettlementService.GetPayableDaysAsync(stall, curYear, curMonth, ct);
                    if (days is { Count: > 0 })
                    {
                        // Resolve base + fish ₱/kg (tenant-aware, as-of the latest payable day) for the UI preview.
                        var quote = await npmMonthSettlementService.QuoteFishDayAsync(stall, days[^1], 0m, ct);
                        items.Add(new PayorPayableItemDto(
                            stall.Id, stall.StallNo, facility, curYear, curMonth,
                            $"{curYear:0000}-{curMonth:00}", 0m, PayorPayableKind.NpmFish,
                            UncollectedDays: days, BaseFee: quote.BaseFee, FishRatePerKilo: quote.FishRatePerKilo));
                    }
                }
                else
                {
                    // Non-fish sections: the REST of the month's stall rent (₱900, or ₱840 once ₱60 was collected), from
                    // the shared settlement service's whole-month quote — the figure initiate charges and settlement marks.
                    // No day count or daily rate is stated: the figure is the month's remainder, not days counted out.
                    var payable = await npmMonthSettlementService.ComputeWholeMonthPayableAsync(stall, curYear, curMonth, ct);
                    if (payable.Amount > 0m)
                    {
                        items.Add(new PayorPayableItemDto(
                            stall.Id, stall.StallNo, facility, curYear, curMonth,
                            $"{curYear:0000}-{curMonth:00}", payable.Amount, PayorPayableKind.NpmDaily));
                    }
                }
            }

            // 4) The months ahead the office lets this account pay, once nothing older stands in the way.
            if (isMonthly && stall.Status == StallStatus.Active
                && (!rules.OldestMonthFirst || !items.Any(i => i.StallId == stall.Id && i.Kind == PayorPayableKind.Monthly && rules.PaidInOrder(i.Year, i.Month, today))))
            {
                foreach (var ahead in rules.MonthsPayableAhead(today))
                {
                    if (stall.OccupancyAnsweringForMonth(ahead.Year, ahead.Month, today)?.Contract.BillsCalendarMonth(ahead.Year, ahead.Month) != true)
                        break;
                    if (recordedAhead.Contains((stall.Id, ahead.Year, ahead.Month)))
                        continue;
                    items.Add(new PayorPayableItemDto(stall.Id, stall.StallNo, facility, ahead.Year, ahead.Month, $"{ahead.Year:0000}-{ahead.Month:00}",
                        stall.MonthlyRentFor(ahead.Year, ahead.Month, today)));
                }
            }
        }

        // The wording a payor reads comes from the stall's own tenant facility record; the slot code stays the identity.
        var facilityOf = stalls.Where(s => s.Facility is not null).ToDictionary(s => s.Id, s => s.Facility!);
        // PayableOnline is the initiate guard's own rule (facility billing basis + kind), so a daily-billed market's balance,
        // whatever its kind, stays visible but read-only.
        var stated = items.Select(i => (facilityOf.TryGetValue(i.StallId, out var facility)
            ? i with
            {
                FacilityName = facility.Name,
                FacilityShortName = string.IsNullOrWhiteSpace(facility.ShortName) ? null : facility.ShortName,
                PayableOnline = i.PayableOnline && InitiateOnlinePaymentCommandHandler.IsOnlineStallRent(facility, i.Kind)
            }
            : i with { PayableOnline = false }) with
            {
                PreviousBalance = new DateOnly(i.Year, i.Month, 1) < monthStart,
                Advance = new DateOnly(i.Year, i.Month, 1) > monthStart,
                DueDate = i.Kind == PayorPayableKind.Monthly ? rules.DueDate(i.Year, i.Month) : null
            }).ToList();

        if (!rules.OldestMonthFirst)
            return stated;

        // The button says what the initiate guard would say: only a stall's oldest counted month can be paid now. A month from before the rules begin never holds up another.
        var oldestPayable = stated.Where(i => i.PayableOnline && rules.PaidInOrder(i.Year, i.Month, today)).GroupBy(i => i.StallId)
            .ToDictionary(g => g.Key, g => g.MinBy(i => (i.Year, i.Month))!.Period);
        return stated.Select(i => i.PayableOnline && rules.PaidInOrder(i.Year, i.Month, today) && oldestPayable[i.StallId] != i.Period
            ? i with { PayableOnline = false, PayFirst = oldestPayable[i.StallId] }
            : i).ToList();
    }
}
