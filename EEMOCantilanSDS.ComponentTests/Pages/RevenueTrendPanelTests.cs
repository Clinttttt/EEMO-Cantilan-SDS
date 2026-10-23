using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Revenue trend view reads the official Monthly Income statement period by period. These tests hold it to that statement:
/// its bars and rows add back to the statement's own totals, annual targets are never prorated into months, partial target coverage
/// is stated, and a year that cannot be read is never drawn as zero.
/// </summary>
public sealed class RevenueTrendPanelTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficialReportsApiClient> _reports = new();

    public RevenueTrendPanelTests()
    {
        Services.AddSingleton(_reports.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    private static IReadOnlyList<MonthlyIncomeCellDto> Cells(params decimal[] canonical) =>
        Enumerable.Range(0, 12).Select(i => new MonthlyIncomeCellDto(0m, i < canonical.Length ? canonical[i] : 0m)).ToList();

    private static OfficialMonthlyIncomeGroupDto Group(string key, string label, params decimal[] canonical)
    {
        var months = Cells(canonical);
        return new(key, label, [], months, new MonthlyIncomeCellDto(0m, months.Sum(m => m.Canonical)));
    }

    private static OfficialMonthlyIncomeDto Statement(int year, TargetCoverageDto? coverage, params OfficialMonthlyIncomeGroupDto[] groups)
    {
        var months = Enumerable.Range(0, 12).Select(i => new MonthlyIncomeCellDto(0m, groups.Sum(g => g.MonthTotals[i].Canonical))).ToList();
        return new(year, null, groups, months, new MonthlyIncomeCellDto(0m, months.Sum(m => m.Canonical)), coverage is not null, [], DateTime.UtcNow, coverage);
    }

    private void Serve(int year, OfficialMonthlyIncomeDto? statement) =>
        _reports.Setup(x => x.GetMonthlyIncomeAsync(year, null)).ReturnsAsync(statement is null
            ? Result<OfficialMonthlyIncomeDto>.Failure("offline") : Result<OfficialMonthlyIncomeDto>.Success(statement));

    [Fact]
    public void Monthly_ShowsEveryElapsedMonth_StackedByGroup_AndTheTableAddsBackToTheStatement()
    {
        var statement = Statement(2024, new TargetCoverageDto(TargetCoverageState.Partial, 2, 3, 120_000m, 0m, null),
            Group("MARKET", "Income From Market", 100m, 200m, 300m),
            Group("SLAUGHTERHOUSE", "Income from Slaughterhouse", 10m, 0m, 20m));
        Serve(2024, statement);

        var cut = RenderComponent<RevenueTrendPanel>(p => p.Add(x => x.Year, 2024).Add(x => x.Month, 2));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(12, cut.FindAll(".bar-group").Count);                       // a past year is read in full: 12 months, chronological
            Assert.Contains("Jan 2024: official income ₱110.00", cut.FindAll(".bar-group")[0].GetAttribute("aria-label"));
            Assert.Equal(2, cut.FindAll(".bar-group")[0].QuerySelectorAll(".bar-seg").Length);   // market + slaughterhouse segments
            Assert.Single(cut.FindAll(".bar-group")[1].QuerySelectorAll(".bar-seg"));            // February had no slaughterhouse money
            Assert.Contains("is-selected", cut.FindAll(".bar-group")[1].QuerySelector(".bar-month")!.GetAttribute("class"));
            Assert.Equal("No collections recorded".ToLowerInvariant(), cut.FindAll(".bar-group")[11].GetAttribute("aria-label")!.Split(": ")[1]);   // a genuine zero, not a missing month
        }, Timeout);

        var rows = cut.FindAll("tbody tr").Select(r => r.TextContent).ToList();
        Assert.Equal(12, rows.Count);
        var footer = cut.Find("tfoot tr").TextContent;
        Assert.Contains("₱630.00", footer);                                          // 100+200+300+10+20, exactly the statement's grand total
        // The annual target is stated once, with its coverage, and never divided into months.
        Assert.DoesNotContain("₱120,000.00", footer);                                  // a partial target is not presented as the target
        Assert.DoesNotContain("₱10,000.00", string.Concat(rows));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighValuesKeepExactAccessibleAmountsAndLabelsAreAnchoredToThePlot(bool annual)
    {
        for (var year = 2020; year < 2024; year++) Serve(year, Statement(year, null, Group("MARKET", "Market", 0m)));
        Serve(2024, Statement(2024, null, Group("MARKET", "Market", 9_999_999m)));
        var cut = RenderComponent<RevenueTrendPanel>(p => p.Add(x => x.Year, 2024).Add(x => x.Annual, annual));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".bar-wrap > .bar-top")), Timeout);
        Assert.Equal("₱9,999,999.00", cut.Find(".bar-top").GetAttribute("title"));
        Assert.Contains(cut.FindAll(".bar-group"), x => x.GetAttribute("aria-label")!.Contains("₱9,999,999.00"));
        Assert.NotEmpty(cut.FindAll(".bar-none"));
        Assert.Empty(cut.FindAll(".bar .bar-top"));
    }

    [Fact]
    public void Annual_ReadsFiveYears_StatesTargetCoverage_AndNeverDrawsAnUnreadableYearAsZero()
    {
        Serve(2020, Statement(2020, null, Group("MARKET", "Income From Market", 50m)));
        Serve(2021, Statement(2021, new TargetCoverageDto(TargetCoverageState.None, 0, 1, 0m, 0m, null), Group("MARKET", "Income From Market", 60m)));
        Serve(2022, null);                                                           // this year cannot be read
        Serve(2023, Statement(2023, new TargetCoverageDto(TargetCoverageState.Complete, 1, 1, 100m, 70m, 70m), Group("MARKET", "Income From Market", 70m)));
        Serve(2024, Statement(2024, new TargetCoverageDto(TargetCoverageState.Partial, 1, 2, 40m, 80m, null), Group("MARKET", "Income From Market", 80m)));

        var cut = RenderComponent<RevenueTrendPanel>(p => p.Add(x => x.Year, 2024).Add(x => x.Annual, true));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Equal(4, rows.Count);                                             // 2022 is absent rather than shown as ₱0.00
            Assert.DoesNotContain("2022", string.Concat(rows.Select(r => r.QuerySelector("th")!.TextContent)));
            Assert.Contains("2022", cut.Find("[role='alert']").TextContent);
            Assert.DoesNotContain("Not set", rows[1].TextContent);
            Assert.Contains("₱100.00", rows[2].TextContent); Assert.Contains("70.0%", rows[2].TextContent);   // 2023: complete, attainment shown
            Assert.DoesNotContain("(partial)", rows[3].TextContent);         // 2024: incomplete coverage is said, with no attainment
            Assert.Equal("—", rows[3].QuerySelectorAll("td").Last().TextContent.Trim());
            Assert.DoesNotContain("Year to date", cut.Markup);
        }, Timeout);
        foreach (var year in new[] { 2020, 2021, 2022, 2023, 2024 }) _reports.Verify(x => x.GetMonthlyIncomeAsync(year, null), Times.Once);
        _reports.Verify(x => x.GetMonthlyIncomeAsync(2019, null), Times.Never);
    }

    [Fact]
    public void ReportAdjustments_AreKeptApartFromCollected_AndAddedOnlyIntoOfficialIncome()
    {
        var months = Cells(100m).Select((c, i) => i == 0 ? c with { AdjustmentAmount = 25m } : c).ToList();
        var market = new OfficialMonthlyIncomeGroupDto("MARKET", "Income From Market", [], months, new MonthlyIncomeCellDto(0m, 100m, 25m));
        var statement = new OfficialMonthlyIncomeDto(2024, null, [market], months, new MonthlyIncomeCellDto(0m, 100m, 25m), false, [], DateTime.UtcNow, null);
        Serve(2024, statement);

        var cut = RenderComponent<RevenueTrendPanel>(p => p.Add(x => x.Year, 2024));

        cut.WaitForAssertion(() =>
        {
            var january = cut.FindAll("tbody tr")[0].TextContent;
            Assert.Contains("₱100.00", january);                                      // collected: the system cash
            Assert.Contains("₱25.00", january);                                       // the report adjustment, apart
            Assert.Contains("₱125.00", january);                                      // official income
            Assert.Contains("official income ₱125.00", cut.FindAll(".bar-group")[0].GetAttribute("aria-label"));
        }, Timeout);
    }
}
