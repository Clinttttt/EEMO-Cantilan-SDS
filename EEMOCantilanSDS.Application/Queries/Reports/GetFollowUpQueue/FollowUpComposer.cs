using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;

/// <summary>
/// Pure builder for the Follow-up action list. The live queue ("as of today") and the history
/// snapshot ("as of a past period") fetch the SAME canonical sources and differ only in how the
/// contract-attention and online-awaiting-OR sources are scoped. Keeping the item-building here (a
/// single pure function over already-fetched inputs) guarantees the two views stay identical in every
/// rule except that intended scoping difference.
/// </summary>
public static class FollowUpComposer
{
    // Stall-based facilities whose monthly compliance feeds the queue.
    public static readonly FacilityCode[] StallFacilities =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE };

    // Sections (urgency bands) — match the page's grouping.
    private const int SecImmediate = 1;
    private const int SecThisPeriod = 2;
    private const int SecVerify = 3;
    private const int SecOperational = 4;

    // Excused days that warrant a review (repeated absence). A fully-excused month is always shown.
    private const int RepeatedAbsentThreshold = 10;

    /// <param name="asOf">The scope date stamped on the DTO (today for the live queue; end-of-period for history).</param>
    /// <param name="facilityReports">Monthly report per <see cref="StallFacilities"/> entry.</param>
    /// <param name="contracts">Contract-attention rows already scoped by the caller (today vs. as-of-period).</param>
    /// <param name="awaitingOr">Online awaiting-OR rows already scoped by the caller (all vs. this period).</param>
    public static FollowUpQueueDto Compose(
        int year,
        int month,
        DateOnly asOf,
        IReadOnlyList<DelinquentStallDto> delinquency,
        IReadOnlyDictionary<FacilityCode, FacilityReportsDto> facilityReports,
        IReadOnlyList<OnlinePaymentAwaitingOrDto> awaitingOr,
        IReadOnlyList<SlaughterTransactionDto> slaughter,
        IReadOnlyList<TrmTripDto> trips,
        IReadOnlyList<TpmVendorAttendanceDto> attendance,
        IReadOnlyList<UnreceiptedPaymentDto> unreceipted,
        IReadOnlyList<ContractAttentionDto> contracts,
        IReadOnlyList<UtilityBill> utilityBills,
        // Balance in full per LAPSED occupancy, keyed by stall identity — a facility-and-number key collapsed the
        // market's three "Stall 1" spaces into one figure. Lets a lapsed row state its whole balance and be
        // payable. Null = none.
        IReadOnlyDictionary<Guid, decimal>? expiredBalances = null,
        // Ended occupancies from the register (closed, lapsed, or handed to another lessee). A lessee whose
        // occupancy ended is no longer the stall's contract holder, so nothing else in this queue can surface
        // their balance — see section 5b. Null = none.
        IReadOnlyList<ClosedStallAccountDto>? endedOccupancies = null,
        // Overrides the period heading. Used by the "Whole time" view, whose figures are cumulative totals rather
        // than one month's snapshot — labelling it with a month would be the very confusion it exists to remove.
        string? periodLabelOverride = null,
        // The window this view is scoped to. Supplied by a year or a month view, whose figures are that period's:
        // a term or an occupancy is then stated as the PART of it that falls inside the window, so the span beside
        // the amount cannot say "Jun 2023 → Jun 7, 2026" under a 2026 heading. Null (both) = the cumulative view,
        // whose figures are lifetime totals and whose spans are therefore whole.
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null,
        // What span the DELINQUENCY figures cover, when it is not the page's own period. A delinquency balance is
        // never one month's: the live queue is handed a rolling twelve months, the whole-time view each account's
        // entire position. Labelling those rows with the page's month made a current-period screen state a
        // multi-month debt as August's — the same scope confusion that put "37 months" under "January – December
        // 2026". Null keeps the page's period label, which is right only where the two genuinely coincide.
        string? delinquencySpanLabel = null)
    {
        var periodLabel = periodLabelOverride
            ?? new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        // The cumulative view is the one with no window: its figures are lifetime totals, so its spans are whole.
        var cumulative = periodStart is null || periodEnd is null;
        var items = new List<FollowUpItemDto>();

        // Stalls whose contract has already lapsed are surfaced under "Contract expired" (section 5).
        // Don't ALSO list them as "current-period unpaid" — that double-lists the same expired account
        // (an expired stall belongs in the contract bucket, not the current-period bucket).
        // Keyed on stall identity, not facility-and-number: the market numbers spaces per section and has three
        // stalls called "1", so a number key made one lapsed term suppress the current-month balance of all three.
        var expiredContractStallIds = contracts
            .Where(c => c.IsExpired)
            .Select(c => c.StallId)
            .ToHashSet();

        // ── 1) Delinquency (3+ = delinquent, 1–2 = arrears). The span is the caller's: a period screen asks for a
        // rolling twelve months, the Financial Reports for each account's whole position. ──
        //
        // These rows are the authoritative statement of a stall's outstanding balance in this list. Later sections
        // may add a row about the same stall for a different reason — a lapsed term needing renewal, say — but they
        // must not state the money a second time, or one debt is counted twice in the page total. Keyed on the
        // stall's identity, because the market numbers spaces per section and has three stalls called "1"; a
        // facility-and-number key made them one.
        var delinquentKeys = new HashSet<string>();
        var moneyStatedForStall = new HashSet<Guid>();
        foreach (var d in delinquency)
        {
            if (d.MonthsUnpaid < 1) continue;
            delinquentKeys.Add(Key(d.FacilityCode, d.StallNo));
            if (d.StallId is { } stallWithMoney) moneyStatedForStall.Add(stallWithMoney);
            var isDelinquent = d.MonthsUnpaid >= DomainRules.DelinquentThresholdMonths;
            items.Add(new FollowUpItemDto(
                Section: isDelinquent ? SecImmediate : SecThisPeriod,
                Priority: isDelinquent ? "Critical" : "Normal",
                Reason: isDelinquent ? "Delinquent" : "Arrears",
                ReasonKind: isDelinquent ? "delinquent" : "arrears",
                Facility: d.FacilityCode,
                Model: Model(d.FacilityCode),
                Person: Named(d.Occupant),
                Identifier: $"Stall {d.StallNo}",
                Amount: d.OutstandingBalance,
                Excused: false,
                Period: delinquencySpanLabel ?? periodLabel,
                Status: $"Unpaid · {d.MonthsUnpaid} month{(d.MonthsUnpaid == 1 ? "" : "s")}",
                Action: "View vendor",
                Link: ProfileLink(d.FacilityCode, d.StallNo),
                StallId: d.StallId));
        }

        // ── 2) Per stall-facility compliance: current-period unpaid/partial, excused, NPM missed-daily ──
        foreach (var code in StallFacilities)
        {
            if (!facilityReports.TryGetValue(code, out var report) || report is null) continue;

            foreach (var s in report.StallCompliance)
            {
                // Excused / absent worth a review: a fully-excused month (NPM "Absent" or monthly
                // "Excused"), or repeated NPM absence.
                if (s.Status is "Absent" or "Excused" || s.AbsentDays >= RepeatedAbsentThreshold)
                {
                    items.Add(new FollowUpItemDto(
                        SecVerify, "Review", "Excused / Absent", "excused",
                        code, Model(code), Named(s.Occupant),
                        s.StallNo.StartsWith("Stall", StringComparison.OrdinalIgnoreCase) ? s.StallNo : $"Stall {s.StallNo}",
                        0m, true, periodLabel,
                        s.Status is "Absent" or "Excused" ? "Excused · full period" : $"Excused · {s.AbsentDays} days",
                        "Verify absence", ProfileLink(code, s.StallNo), s.StallId));
                    continue;
                }

                // Current-period unpaid / partial. Stated even for a stall that also appears under delinquency or
                // arrears: those figures cover months that have ALREADY elapsed and deliberately exclude the month
                // in progress, so the two do not overlap. Suppressing this row hid the current month's balance
                // altogether — invisible while the delinquency list was near-empty, and money off the screen once
                // that list started reporting every month a payor actually owed.
                var isUnpaid = s.Status == "Unpaid";
                var isPartial = s.Status == "Partial";
                if ((isUnpaid || isPartial) && s.Balance > 0m
                    && !expiredContractStallIds.Contains(s.StallId))
                {
                    items.Add(new FollowUpItemDto(
                        SecThisPeriod, "Normal",
                        isPartial ? "Partial payment" : "Current-period unpaid",
                        "current",
                        code, Model(code), Named(s.Occupant), $"Stall {s.StallNo}",
                        s.Balance, false, periodLabel,
                        isPartial ? "Partial" : "Unpaid",
                        "View vendor", ProfileLink(code, s.StallNo), s.StallId));
                }
            }

            // NPM daily coverage gap (missed collection days this period).
            if (code == FacilityCode.NPM && report.DailyCollectionStreak is { } streak && streak.MissedDays > 0)
            {
                items.Add(new FollowUpItemDto(
                    SecOperational, streak.MissedDays >= 5 ? "High" : "Normal",
                    "NPM missed collection", "npm",
                    FacilityCode.NPM, Model(FacilityCode.NPM), "New Public Market",
                    $"{streak.MissedDays} missed day{(streak.MissedDays == 1 ? "" : "s")}",
                    null, false, periodLabel,
                    "Daily coverage gap", "Open daily calendar", "/npm"));
            }
        }

        // Utility balances (NPM electricity/water): show each unpaid/partial utility as its own
        // action row. The current domain still stores one OR number on the bill, but the follow-up
        // presentation deliberately separates electricity from water so admin work is clear.
        foreach (var bill in utilityBills)
        {
            // Each bill states its own month. A whole-year view gathers twelve months of bills, and labelling them
            // all with the view's heading would hide which month a balance belongs to.
            var billLabel = new DateTime(bill.BillingYear, bill.BillingMonth, 1)
                .ToString("MMMM yyyy", CultureInfo.InvariantCulture);

            AddUtilityBalance(items, bill, "Electricity", bill.ElecStatus, bill.ElecBalanceDue, bill.ElecConsumption, "kWh", billLabel);
            AddUtilityBalance(items, bill, "Water", bill.WaterStatus, bill.WaterBalanceDue, bill.WaterConsumption, "cu.m.", billLabel);
        }

        // ── 3) Missing OR — online payments received but not yet receipted ──
        foreach (var a in awaitingOr)
        {
            items.Add(new FollowUpItemDto(
                SecImmediate, "High", "Missing OR", "missingor",
                a.Facility, Model(a.Facility), Named(a.PayorName),
                $"Stall {a.StallNo} · online",
                a.Amount, false, a.Period,
                "Paid · awaiting OR", "Encode OR", "/online-payments"));
        }

        // ── 4) Missing OR — service facilities (paid/recorded with a blank receipt), per record month ──
        // Grouped by (payor, year, month) so a whole-year view lists one row per month (each opens the
        // right month in Add-OR); a single-month view collapses to one group with the same label as before.
        foreach (var g in slaughter.Where(t => string.IsNullOrWhiteSpace(t.ORNumber))
                     .GroupBy(t => new { Owner = Named(t.OwnerName), t.TransactionDate.Year, t.TransactionDate.Month }))
        {
            // A receipt = one visit (owner + date); a visit may span several animal-type rows.
            var receipts = g.Select(t => t.TransactionDate).Distinct().Count();
            items.Add(new FollowUpItemDto(
                SecOperational, "Normal", "Missing OR", "missingor",
                FacilityCode.SLH, Model(FacilityCode.SLH), g.Key.Owner,
                $"{receipts} receipt{(receipts == 1 ? "" : "s")}",
                g.Sum(t => t.TotalAmount), false, MonthLabel(g.Key.Year, g.Key.Month),
                "Recorded · OR blank", "Add OR", "/slh"));
        }

        foreach (var g in trips.Where(t => string.IsNullOrWhiteSpace(t.ORNumber))
                     .GroupBy(t =>
                     {
                         var ph = PhilippineTime.ToPhilippineTime(t.RecordedAt);   // trip's business (PH) month
                         return new { Driver = Named(t.DriverName), ph.Year, ph.Month };
                     }))
        {
            items.Add(new FollowUpItemDto(
                SecOperational, "Normal", "Trip awaiting OR", "missingor",
                FacilityCode.TRM, Model(FacilityCode.TRM), g.Key.Driver,
                $"{g.Count()} trip{(g.Count() == 1 ? "" : "s")}",
                g.Sum(t => t.Fee), false, MonthLabel(g.Key.Year, g.Key.Month),
                "Paid · OR blank", "Add OR", "/trm"));
        }

        foreach (var g in attendance.Where(a => a.IsPaid && string.IsNullOrWhiteSpace(a.ORNumber))
                     .GroupBy(a => new { Vendor = Named(a.VendorName), a.MarketDate.Year, a.MarketDate.Month }))
        {
            items.Add(new FollowUpItemDto(
                SecOperational, "Normal", "Market-day · OR", "missingor",
                FacilityCode.TPM, Model(FacilityCode.TPM), g.Key.Vendor,
                $"{g.Count()} market day{(g.Count() == 1 ? "" : "s")}",
                g.Sum(a => a.Fee), false, MonthLabel(g.Key.Year, g.Key.Month),
                "Paid · OR blank", "Add OR", "/tpm"));
        }

        // ── 4b) Missing OR — cash/field records fully paid but not yet receipted ──
        // Monthly cash records are immediate traceability; NPM daily receipts are operational. Online
        // payments are excluded by the repository (they have their own awaiting-OR queue above).
        foreach (var u in unreceipted)
        {
            // Whole-year aggregation returns one row per (stall, month); label each with its own month.
            // Single-month callers leave Year/Month unset (0) → falls back to the view's period label.
            var uPeriod = u.Month is >= 1 and <= 12
                ? new DateTime(u.Year, u.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture)
                : periodLabel;

            if (u.IsDaily)
            {
                items.Add(new FollowUpItemDto(
                    SecOperational, "Normal", "Daily receipt · OR", "missingor",
                    u.Facility, Model(u.Facility), Named(u.Occupant),
                    $"Stall {u.StallNo} · {u.Count} day{(u.Count == 1 ? "" : "s")}",
                    u.Amount, false, uPeriod,
                    "Paid daily · OR blank", "Add OR", "/npm",
                    StallId: u.StallId));
            }
            else
            {
                items.Add(new FollowUpItemDto(
                    SecImmediate, "High", "Missing OR", "missingor",
                    u.Facility, Model(u.Facility), Named(u.Occupant), $"Stall {u.StallNo}",
                    u.Amount, false, uPeriod,
                    "Paid · OR blank", "Add OR", ProfileLink(u.Facility, u.StallNo),
                    StallId: u.StallId));
            }
        }

        // ── 5) Contract attention — expired / expiring-soon contracts with an active occupant ──

        // What an expired row owes. A cumulative view is given the register's lifetime balances and states those; a
        // view scoped to a period must state THAT period's figure instead, which the facility assessments already
        // hold — a lifetime total shown under a single year's heading is the confusion the office reported.
        var periodBalances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, report) in facilityReports)
        {
            foreach (var s in report.StallCompliance.Where(s => s.Balance > 0m))
                periodBalances[Key(code, s.StallNo)] = s.Balance;
        }

        foreach (var c in contracts)
        {
            var key = Key(c.FacilityCode, c.StallNo);

            // A lapsed term needs renewing, which is why this row exists — but if a delinquency or arrears row above
            // already states this stall's outstanding balance, this row must state none. Nora M. Doloriel's stall 20
            // read ₱33,300 as Delinquent AND ₱5,400 as Contract expired: one debt, two money rows, ₱38,700
            // contributed to the header for a ₱33,300 account. The row keeps its status, its period and its action;
            // only the amount belongs to the row that owns it.
            var moneyAlreadyStated = moneyStatedForStall.Contains(c.StallId);

            var contractBalance = !c.IsExpired || moneyAlreadyStated
                ? null
                : expiredBalances is not null && expiredBalances.TryGetValue(c.StallId, out var lifetime) && lifetime > 0m
                    ? lifetime
                    : periodBalances.TryGetValue(key, out var forPeriod) && forPeriod > 0m
                        ? forPeriod
                        : (decimal?)null;

            var expiredOn = c.ExpiryDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

            // The period a lapsed row states: the part of the term that falls inside the view's window. Under a
            // 2026 filter a term of Jun 2023 → Jun 7, 2026 reads "Jan 1, 2026 → Jun 7, 2026" — the months the
            // amount beside it was assessed for. The whole term belongs to the cumulative view, whose figure is
            // the lifetime balance; stating it under a single year's heading said the opposite of the amount.
            var expiredPeriod = SpanLabel(c.EffectivityDate, c.ExpiryDate, periodStart, periodEnd);

            // The day the term lapsed is why the row is here, so a period-scoped row keeps it in the status line
            // even when the span was clipped before it. The cumulative row already ends its span on that day.
            var expiredStatus = cumulative ? "Active occupant" : $"Expired {expiredOn} · active occupant";

            items.Add(new FollowUpItemDto(
                c.IsExpired ? SecImmediate : SecThisPeriod,
                c.IsExpired ? "High" : "Normal",
                c.IsExpired ? "Contract expired" : "Contract expiring",
                "contract",
                c.FacilityCode, Model(c.FacilityCode), Named(c.Occupant), $"Stall {c.StallNo}",
                contractBalance, false,
                c.IsExpired ? expiredPeriod : expiredOn,
                c.IsExpired ? expiredStatus : "Expiring soon",
                "Review contract", ProfileLink(c.FacilityCode, c.StallNo),
                StallId: c.StallId));
        }

        // ── 5b) Ended occupancies that still owe money ──
        // A lessee whose occupancy has ENDED — the stall handed to someone else, so their contract is no longer
        // the stall's contract — appears nowhere above, yet the register shows their balance in full. Without this
        // the same debt read ₱31,980 on the register and only the current period's ₱210 here: a disagreement the
        // office cannot act on. One row per outstanding account that nothing above already covers.
        if (endedOccupancies is not null)
        {
            // Keyed on the stall's identity where it is known. The market numbers spaces per section, so NPM has
            // three stalls called "1"; a facility-and-number key made them collide, and an ended occupancy on one
            // of them was silently dropped because a different stall sharing the number was already listed. The
            // number key is kept as a fallback for rows that carry no identity.
            var listedStallIds = items
                .Where(i => i.StallId is not null)
                .Select(i => i.StallId!.Value)
                .ToHashSet();
            var listedByNumber = items
                .Where(i => i.StallId is null)
                .Select(i => Key(i.Facility, i.Identifier.Replace("Stall ", string.Empty)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var account in endedOccupancies.Where(a => a.Uncollected > 0m))
            {
                // A LAPSED account is the occupancy still in force, so a delinquency row above already states its
                // balance — listing it again would count one debt twice. A Renewed, Handed-over or Closed account is
                // a DIFFERENT occupancy of the same stall: its debt is its own, owed for a different span and
                // possibly by a different person, so it keeps its row even when the current term also owes. Stall 23
                // is both — Vincent's ₱840 for July under his new term, and ₱32,430 left by the term before it.
                var sameOccupancyAsLive = account.State == InactiveAccountState.Lapsed;

                if (sameOccupancyAsLive
                    && (listedStallIds.Contains(account.StallId)
                        || listedByNumber.Contains(Key(account.FacilityCode, account.StallNo))))
                    continue;

                var ended = account.OccupancyEndedOn ?? account.ClosedOn ?? account.ExpiryDate;

                items.Add(new FollowUpItemDto(
                    SecImmediate,
                    "High",
                    account.State == InactiveAccountState.Closed ? "Closed account balance" : "Past occupancy balance",
                    "contract",
                    account.FacilityCode, Model(account.FacilityCode), Named(account.Occupant), $"Stall {account.StallNo}",
                    account.Uncollected, false,
                    // The part of this occupancy that falls inside the view's window, beside the figure that window
                    // assessed. The cumulative view states the whole occupancy against the whole balance.
                    SpanLabel(account.EffectivityDate, ended, periodStart, periodEnd),
                    // On the cumulative view the figure is the account's whole balance and the row says so; a
                    // period view's figure is that period's, so the qualifier would be untrue there.
                    cumulative ? "No longer the occupant · balance in full" : "No longer the occupant",
                    "Review account", ProfileLink(account.FacilityCode, account.StallNo),
                    StallId: account.StallId,
                    // The term this balance belongs to. Without it, a payment recorded from this row would apply to
                    // whoever holds the stall now — settling the sitting lessee's days under a former lessee's name.
                    ContractId: account.ContractId == Guid.Empty ? null : account.ContractId));
            }
        }

        // Stable order: by section, then priority, then amount (largest first).
        var ordered = items
            .OrderBy(i => i.Section)
            .ThenBy(i => PriorityRank(i.Priority))
            .ThenByDescending(i => i.Amount ?? 0m)
            .ToList();

        return new FollowUpQueueDto(periodLabel, asOf, ordered);
    }

    private static string Key(FacilityCode code, string stallNo) => $"{code}|{stallNo}";

    private static string Named(string? value) => string.IsNullOrWhiteSpace(value) ? "Unnamed occupant" : value;

    private static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// A term or an occupancy stated for the view that is showing it. With a window (a year, a month) it is the
    /// PART that falls inside that window, because the amount beside it is that window's; without one — the
    /// cumulative view — it is the whole span. A span that lies entirely outside the window is left whole: a term
    /// that lapsed before the period is a fact about the past, and clipping it to nothing would say less than
    /// nothing. The span's own beginning is written as a month, the way a term is written; a beginning clipped by
    /// the filter is written with its day, so it cannot be read as the day the occupancy began.
    /// </summary>
    private static string SpanLabel(DateOnly start, DateOnly end, DateOnly? windowStart, DateOnly? windowEnd)
    {
        var from = start;
        var to = end < start ? start : end;

        if (windowStart is { } ws && windowEnd is { } we && ws <= to && from <= we)
        {
            if (ws > from) from = ws;
            if (we < to) to = we;
        }

        var fromText = from == start
            ? from.ToString("MMM yyyy", CultureInfo.InvariantCulture)
            : from.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        return $"{fromText} → {to.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
    }

    private static string ProfileLink(FacilityCode code, string stallNo) =>
        $"/profile/{code.ToString().ToLowerInvariant()}/{stallNo}";

    private static void AddUtilityBalance(
        List<FollowUpItemDto> items,
        UtilityBill bill,
        string utilityName,
        PaymentStatus status,
        decimal balance,
        decimal consumption,
        string unit,
        string periodLabel)
    {
        if (balance <= 0m || status == PaymentStatus.Paid)
            return;

        var stallNo = bill.Stall?.StallNo ?? bill.StallId.ToString("N")[..8];
        var occupant = bill.Stall?.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant;
        var statusLabel = status == PaymentStatus.Partial ? "Partial" : "Unpaid";
        var consumptionLabel = consumption > 0m
            ? $"{consumption:N2} {unit} used"
            : "No recorded consumption";

        items.Add(new FollowUpItemDto(
            SecThisPeriod,
            "Normal",
            $"{utilityName} balance",
            "misc",
            FacilityCode.NPM,
            "Utility billing",
            Named(occupant),
            $"Stall {stallNo} - {utilityName}",
            balance,
            false,
            periodLabel,
            $"{statusLabel} - {consumptionLabel}",
            "Pay Bill",
            "/npm",
            StallId: bill.StallId,
            // The dialog this row opens must offer only the utilities the stall is billed for.
            StallHasElectricity: bill.Stall?.Fees.HasFlag(ApplicableFees.Electricity) ?? false,
            StallHasWater: bill.Stall?.Fees.HasFlag(ApplicableFees.Water) ?? false));
    }

    private static int PriorityRank(string priority) => priority switch
    {
        "Critical" => 0,
        "High" => 1,
        "Normal" => 2,
        _ => 3
    };

    private static string Model(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "Daily stall",
        FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE => "Monthly rental",
        FacilityCode.SLH => "Per-head",
        FacilityCode.TRM => "Per-trip",
        FacilityCode.TPM => "Weekly market",
        _ => "—"
    };
}
