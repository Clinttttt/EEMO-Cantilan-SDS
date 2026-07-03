namespace EEMOCantilanSDS.Application.Common.Caching;

public sealed class EemoCacheOptions
{
    public long SizeLimit { get; init; } = 1024;
    public long EntrySize { get; init; } = 1;

    public TimeSpan DashboardOverviewTtl { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan FacilitySummariesTtl { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan FinancialReportTtl { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MonthEndReportTtl { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan FollowUpHistoryTtl { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan StallHolderListTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ClosedAccountsTtl { get; init; } = TimeSpan.FromMinutes(5);
}
