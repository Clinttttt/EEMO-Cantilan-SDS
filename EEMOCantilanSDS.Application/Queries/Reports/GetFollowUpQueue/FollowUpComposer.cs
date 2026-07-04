using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
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
        IReadOnlyList<UtilityBill> utilityBills)
    {
        var periodLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var items = new List<FollowUpItemDto>();

        // ── 1) Delinquency (rolling 12-mo, excludes current month): 3+ = delinquent, 1–2 = arrears ──
        var delinquentKeys = new HashSet<string>();
        foreach (var d in delinquency)
        {
            if (d.MonthsUnpaid < 1) continue;
            delinquentKeys.Add(Key(d.FacilityCode, d.StallNo));
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
                Period: periodLabel,
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

                // Current-period unpaid / partial — skip stalls already surfaced under delinquency/arrears.
                var isUnpaid = s.Status == "Unpaid";
                var isPartial = s.Status == "Partial";
                if ((isUnpaid || isPartial) && s.Balance > 0m && !delinquentKeys.Contains(Key(code, s.StallNo)))
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
            AddUtilityBalance(items, bill, "Electricity", bill.ElecStatus, bill.ElecBalanceDue, bill.ElecConsumption, "kWh", periodLabel);
            AddUtilityBalance(items, bill, "Water", bill.WaterStatus, bill.WaterBalanceDue, bill.WaterConsumption, "cu.m.", periodLabel);
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

        // ── 4) Missing OR — service facilities (paid/recorded this period with a blank receipt) ──
        foreach (var g in slaughter.Where(t => string.IsNullOrWhiteSpace(t.ORNumber))
                     .GroupBy(t => Named(t.OwnerName)))
        {
            // A receipt = one visit (owner + date); a visit may span several animal-type rows.
            var receipts = g.Select(t => t.TransactionDate).Distinct().Count();
            items.Add(new FollowUpItemDto(
                SecOperational, "Normal", "Missing OR", "missingor",
                FacilityCode.SLH, Model(FacilityCode.SLH), g.Key,
                $"{receipts} receipt{(receipts == 1 ? "" : "s")}",
                g.Sum(t => t.TotalAmount), false, periodLabel,
                "Recorded · OR blank", "Add OR", "/slh"));
        }

        foreach (var g in trips.Where(t => string.IsNullOrWhiteSpace(t.ORNumber))
                     .GroupBy(t => Named(t.DriverName)))
        {
            items.Add(new FollowUpItemDto(
                SecOperational, "Normal", "Trip awaiting OR", "missingor",
                FacilityCode.TRM, Model(FacilityCode.TRM), g.Key,
                $"{g.Count()} trip{(g.Count() == 1 ? "" : "s")}",
                g.Sum(t => t.Fee), false, periodLabel,
                "Paid · OR blank", "Add OR", "/trm"));
        }

        foreach (var g in attendance.Where(a => a.IsPaid && string.IsNullOrWhiteSpace(a.ORNumber))
                     .GroupBy(a => Named(a.VendorName)))
        {
            items.Add(new FollowUpItemDto(
                SecOperational, "Normal", "Market-day · OR", "missingor",
                FacilityCode.TPM, Model(FacilityCode.TPM), g.Key,
                $"{g.Count()} market day{(g.Count() == 1 ? "" : "s")}",
                g.Sum(a => a.Fee), false, periodLabel,
                "Paid · OR blank", "Add OR", "/tpm"));
        }

        // ── 4b) Missing OR — cash/field records fully paid but not yet receipted ──
        // Monthly cash records are immediate traceability; NPM daily receipts are operational. Online
        // payments are excluded by the repository (they have their own awaiting-OR queue above).
        foreach (var u in unreceipted)
        {
            if (u.IsDaily)
            {
                items.Add(new FollowUpItemDto(
                    SecOperational, "Normal", "Daily receipt · OR", "missingor",
                    u.Facility, Model(u.Facility), Named(u.Occupant),
                    $"Stall {u.StallNo} · {u.Count} day{(u.Count == 1 ? "" : "s")}",
                    u.Amount, false, periodLabel,
                    "Paid daily · OR blank", "Add OR", "/npm",
                    StallId: u.StallId));
            }
            else
            {
                items.Add(new FollowUpItemDto(
                    SecImmediate, "High", "Missing OR", "missingor",
                    u.Facility, Model(u.Facility), Named(u.Occupant), $"Stall {u.StallNo}",
                    u.Amount, false, periodLabel,
                    "Paid · OR blank", "Add OR", ProfileLink(u.Facility, u.StallNo),
                    StallId: u.StallId));
            }
        }

        // ── 5) Contract attention — expired / expiring-soon contracts with an active occupant ──
        foreach (var c in contracts)
        {
            items.Add(new FollowUpItemDto(
                c.IsExpired ? SecImmediate : SecThisPeriod,
                c.IsExpired ? "High" : "Normal",
                c.IsExpired ? "Contract expired" : "Contract expiring",
                "contract",
                c.FacilityCode, Model(c.FacilityCode), Named(c.Occupant), $"Stall {c.StallNo}",
                null, false,
                c.ExpiryDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
                c.IsExpired ? "Active occupant" : "Expiring soon",
                "Review contract", ProfileLink(c.FacilityCode, c.StallNo)));
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
            StallId: bill.StallId));
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
