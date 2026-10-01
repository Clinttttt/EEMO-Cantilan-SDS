using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Components.Pages.Menus;

/// <summary>
/// The Arrears workspace and report read existing authoritative receivables only — no new ledger, no second debt
/// calculation, no obligation created. The position is the financial report's own delinquency totals (every account with
/// one or more fully elapsed unpaid months, counted on the server); the register is the follow-up queue's delinquent rows
/// (the same reader Financial Reports → Receivables uses); recovered arrears is the official Monthly Income "Arrears" row.
/// The age-based Arrears qualification is unresolved (Decision Registry), so no Arrears-qualified count is invented.
/// </summary>
public static class ArrearsReads
{
    public sealed record Snapshot(
        int? AccountsTotal,
        decimal? OutstandingTotal,
        int? ArrearsQualifiedAccounts,
        IReadOnlyList<FollowUpItemDto>? Accounts,
        decimal? RecoveredThisMonth,
        bool Failed);

    public static async Task<Snapshot> LoadAsync(IReportsApiClient reports, IOfficialReportsApiClient official, DateOnly month)
    {
        int? accounts = null;
        decimal? outstanding = null;
        int? qualified = null;
        IReadOnlyList<FollowUpItemDto>? rows = null;
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
            }
            else failed = true;
        }
        catch { failed = true; }

        try
        {
            var queue = await reports.GetFollowUpQueueAsync(month.Year, month.Month);
            if (queue is { IsSuccess: true, Value: { } q })
                rows = q.Items.Where(i => i.ReasonKind == "delinquent").ToList();
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

        return new Snapshot(accounts, outstanding, qualified, rows, recovered, failed);
    }
}
