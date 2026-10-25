using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Components.Pages.Menus;

/// <summary>
/// The Arrears workspace and report read existing authoritative receivables only — no new ledger, no second debt
/// calculation, no obligation created. Obligations are classified on the server, one by one (IA-019, IA-020, IA-069):
/// Arrears is unpaid previous-calendar-year rent plus unpaid rent of ended occupancies and expired contracts; Delinquent is the
/// fully elapsed unpaid months of the current year under a live account. The position is the financial report's own totals for
/// each category; the register is the whole-account follow-up rows of the same two kinds (the same reader the Financial Reports
/// use); recovered arrears is the official Monthly Income "Arrears" row. One account can appear under both kinds, each with its own money.
/// </summary>
public static class ArrearsReads
{
    public sealed record Snapshot(
        int? AccountsTotal,
        decimal? OutstandingTotal,
        int? ArrearsQualifiedAccounts,
        IReadOnlyList<FollowUpItemDto>? Accounts,
        decimal? RecoveredThisMonth,
        bool Failed,
        decimal? ArrearsOutstanding = null,
        IReadOnlyList<FollowUpItemDto>? ArrearsAccounts = null,
        // Space rentals, from their own typed feed (per period, classified by the server; earlier renewed terms flagged). Not in the financial-report position.
        IReadOnlyList<ObligationFollowUpItemDto>? SpaceItems = null);

    /// <summary>The stall or space name without its section suffix ("Stall 7 · Corner" reads "Stall 7").</summary>
    public static string StallLabel(FollowUpItemDto item) => item.Identifier.Split(" · ", 2)[0];

    /// <summary>The status as the registers state it: "Unpaid · 4 months" (a daily-billed stall is counted in months like the rest).</summary>
    public static string StatusLabel(FollowUpItemDto item) =>
        System.Text.RegularExpressions.Regex.Replace(item.Status, @"daily fees across (\d+ months?)", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Each account's total outstanding balance across all periods, read the way the account review reads it (the stall's outstanding months),
    /// a few at a time. An account whose read fails is absent: the register shows a dash, never a guessed figure.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(Guid Stall, Guid? Contract), decimal>> AllPeriodsAsync(IPaymentsApiClient payments, IEnumerable<FollowUpItemDto> items)
    {
        var keys = items.Where(i => i.StallId.HasValue).Select(i => (Stall: i.StallId!.Value, Contract: i.ContractId)).Distinct().ToList();
        var totals = new System.Collections.Concurrent.ConcurrentDictionary<(Guid Stall, Guid? Contract), decimal>();
        await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (key, _) =>
        {
            try
            {
                var result = await payments.GetOutstandingMonthsAsync(key.Stall, key.Contract);
                if (result is { IsSuccess: true, Value: { } months })
                    totals[key] = months.Where(p => !p.IsExcused && p.TotalBill > 0).Sum(p => p.BalanceDue);
            }
            catch { /* this account shows a dash */ }
        });
        return totals;
    }

    public static async Task<Snapshot> LoadAsync(IReportsApiClient reports, IOfficialReportsApiClient official, DateOnly month)
    {
        int? accounts = null;
        decimal? outstanding = null;
        int? qualified = null;
        decimal? arrearsOutstanding = null;
        IReadOnlyList<FollowUpItemDto>? rows = null;
        IReadOnlyList<FollowUpItemDto>? arrearsRows = null;
        IReadOnlyList<ObligationFollowUpItemDto>? spaceItems = null;
        decimal? recovered = null;
        var failed = false;

        try
        {
            var position = await reports.GetFinancialReportAsync(ReportPeriod.Monthly, month.Year, month.Month);
            if (position is { IsSuccess: true, Value: { } report })
            {
                accounts = report.DelinquentAccountsTotal;
                outstanding = report.DelinquentOutstandingTotal;
                qualified = report.ArrearsAccountsTotal;
                arrearsOutstanding = report.ArrearsOutstandingTotal;
            }
            else failed = true;
        }
        catch { failed = true; }

        try
        {
            // Whole account: previous-year Arrears and every ended occupancy are only complete over all time, so this is the cumulative follow-up view.
            var queue = await reports.GetFollowUpHistoryAsync(month.Year, month.Month, wholeYear: false, allTime: true);
            if (queue is { IsSuccess: true, Value: { } q })
            {
                rows = q.Items.Where(i => i.ReasonKind == "delinquent").ToList();
                arrearsRows = q.Items.Where(i => i.ReasonKind == "arrears").ToList();
                spaceItems = q.ObligationItems ?? [];
            }
            else failed = true;
        }
        catch { failed = true; }

        try
        {
            var income = await official.GetMonthlyIncomeAsync(month.Year, month.Month);
            if (income is { IsSuccess: true, Value: { } statement })
            {
                var row = statement.Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Key == "ARREARS");
                recovered = row?.Months[month.Month - 1].Total ?? 0m;
            }
        }
        catch { /* recovered stays unavailable ("—") */ }

        return new Snapshot(accounts, outstanding, qualified, rows, recovered, failed, arrearsOutstanding, arrearsRows, spaceItems);
    }
}
