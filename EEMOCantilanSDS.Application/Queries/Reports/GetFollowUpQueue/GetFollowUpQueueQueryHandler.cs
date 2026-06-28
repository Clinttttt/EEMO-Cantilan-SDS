using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;

/// <summary>
/// Assembles the admin Follow-up Queue by composing the SAME canonical sources used elsewhere — no new
/// aggregation or financial logic is introduced:
///   • Delinquent (3+) / Arrears (1–2) ← shared rolling-window delinquency.
///   • Current-period unpaid / partial, Excused (NPM), and NPM missed-daily ← per-facility report.
///   • Missing OR (online) ← online-payments awaiting-OR reconciliation queue.
///   • Missing OR (SLH/TRM/TPM) ← each service facility's month records with a blank receipt.
///   • Contract attention ← occupied stalls whose active contract is expired / expiring soon.
/// Items are grouped into four urgency sections and sorted by priority; scope is "as of" the period.
/// </summary>
public class GetFollowUpQueueQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IStallRepository stallRepository,
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    ISlaughterRepository slaughterRepository,
    ITrmRepository trmRepository,
    ITpmRepository tpmRepository
) : IRequestHandler<GetFollowUpQueueQuery, Result<FollowUpQueueDto>>
{
    private static readonly FacilityCode[] StallFacilities =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE };

    // Sections (urgency bands) — match the page's grouping.
    private const int SecImmediate = 1;
    private const int SecThisPeriod = 2;
    private const int SecVerify = 3;
    private const int SecOperational = 4;

    // Excused days that warrant a review (repeated absence). A fully-excused month is always shown.
    private const int RepeatedAbsentThreshold = 10;

    public async Task<Result<FollowUpQueueDto>> Handle(GetFollowUpQueueQuery request, CancellationToken ct)
    {
        var year = request.Year;
        var month = request.Month;
        var periodLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        var items = new List<FollowUpItemDto>();

        // ── 1) Delinquency (rolling 12-mo, excludes current month): 3+ = delinquent, 1–2 = arrears ──
        var delinquency = await reportsRepository.GetDelinquentStallsAsync(null, year, month, ct);
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
                Link: ProfileLink(d.FacilityCode, d.StallNo)));
        }

        // ── 2) Per stall-facility compliance: current-period unpaid/partial, excused, NPM missed-daily ──
        foreach (var code in StallFacilities)
        {
            var report = await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, ct);

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
                        "Verify absence", ProfileLink(code, s.StallNo)));
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
                        "View vendor", ProfileLink(code, s.StallNo)));
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

        // ── 3) Missing OR — online payments received but not yet receipted ──
        var awaitingOr = await onlinePaymentRepository.GetAwaitingOrAsync(ct);
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
        var slaughter = await slaughterRepository.GetTransactionsByMonthAsync(year, month, ct);
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

        var trips = await trmRepository.GetTripsByMonthAsync(year, month, ct);
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

        var attendance = await tpmRepository.GetMonthAttendanceAsync(year, month, ct);
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
        var unreceipted = await paymentRepository.GetUnreceiptedCashPaymentsAsync(year, month, ct);
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
        var contracts = await stallRepository.GetContractAttentionAsync(DomainRules.ExpiringSoonMonths, ct);
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

        var dto = new FollowUpQueueDto(periodLabel, PhilippineTime.Today, ordered);
        return Result<FollowUpQueueDto>.Success(dto);
    }

    // Contract expiry warning window and delinquency threshold come from DomainRules (single source).

    private static string Key(FacilityCode code, string stallNo) => $"{code}|{stallNo}";

    private static string Named(string? value) => string.IsNullOrWhiteSpace(value) ? "Unnamed occupant" : value;

    private static string ProfileLink(FacilityCode code, string stallNo) =>
        $"/profile/{code.ToString().ToLowerInvariant()}/{stallNo}";

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
