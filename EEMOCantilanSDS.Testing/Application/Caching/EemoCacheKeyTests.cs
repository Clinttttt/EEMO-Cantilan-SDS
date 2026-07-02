using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Caching;

public class EemoCacheKeyTests
{
    [Fact]
    public void FinancialReportKey_NormalizesTenant_AndIncludesScope()
    {
        var key = EemoCacheKeys.FinancialReport(
            "  CANTILAN-SDS  ",
            ReportPeriod.Monthly,
            2026,
            6,
            FacilityCode.NPM);

        Assert.Equal("cantilan-sds:reports:financial:monthly:2026:06:npm", key);
    }

    [Fact]
    public void FinancialReportRegions_YearlyReport_SubscribeToEveryMonth()
    {
        var regions = EemoCacheRegions.FinancialReportRegions(
            "tenant",
            ReportPeriod.Yearly,
            2026,
            null,
            FacilityCode.TRM);

        Assert.Contains(EemoCacheRegions.ReferenceData("tenant"), regions);
        Assert.Contains(EemoCacheRegions.ActivityFeed("tenant"), regions);
        Assert.Contains(EemoCacheRegions.Period("tenant", 2026, 1), regions);
        Assert.Contains(EemoCacheRegions.Reports("tenant", 2026, 12), regions);
        Assert.Contains(EemoCacheRegions.FacilityPeriod("tenant", FacilityCode.TRM, 2026, 6), regions);
    }

    [Fact]
    public void DashboardRegions_SubscribeToActivityFeed()
    {
        var regions = EemoCacheRegions.DashboardOverviewRegions("tenant", 2026, 6);

        Assert.Contains(EemoCacheRegions.ActivityFeed("tenant"), regions);
    }

    [Fact]
    public void MonthEndReportKey_AndRegions_IncludePeriodScope()
    {
        var key = EemoCacheKeys.MonthEndReport("tenant", 2026, 6);
        var regions = EemoCacheRegions.MonthEndReportRegions("tenant", 2026, 6);

        Assert.Equal("tenant:reports:month-end:2026:06", key);
        Assert.Contains(EemoCacheRegions.Period("tenant", 2026, 6), regions);
        Assert.Contains(EemoCacheRegions.Reports("tenant", 2026, 6), regions);
    }

    [Fact]
    public void FollowUpHistoryKey_AndRegions_IncludePeriodScope()
    {
        var key = EemoCacheKeys.FollowUpHistory("tenant", 2025, 12);
        var regions = EemoCacheRegions.FollowUpHistoryRegions("tenant", 2025, 12);

        Assert.Equal("tenant:reports:follow-up-history:2025:12", key);
        Assert.Contains(EemoCacheRegions.Period("tenant", 2025, 12), regions);
        Assert.Contains(EemoCacheRegions.Reports("tenant", 2025, 12), regions);
    }

    [Fact]
    public void StallHolderListKey_SeparatesFacilitySectionAndSearch()
    {
        var key = EemoCacheKeys.StallHolderList(
            "tenant",
            FacilityCode.NPM,
            MarketSection.FishSection,
            " Stall 12 ");

        Assert.Equal("tenant:stalls:holders:npm:fishsection:stall%2012", key);
    }

    [Fact]
    public void ClosedAccountsRegions_SubscribeToReferenceData()
    {
        var regions = EemoCacheRegions.ClosedAccountsRegions("tenant");

        Assert.Equal("tenant:stalls:closed-accounts", EemoCacheKeys.ClosedAccounts("tenant"));
        Assert.Contains(EemoCacheRegions.ReferenceData("tenant"), regions);
        Assert.Contains(EemoCacheRegions.ActivityFeed("tenant"), regions);
    }
}
