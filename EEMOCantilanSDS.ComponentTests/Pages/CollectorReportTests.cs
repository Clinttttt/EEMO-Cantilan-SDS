using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using CollectorReport = EEMOCantilanSDS.Client.Components.Pages.Reports.CollectorReport;

/// <summary>
/// The Report of Collections as a document.
///
/// <para>
/// Two properties matter more than any single figure. A sheet opened without a collector must state that rather than print
/// anything, because a document that names no accountable officer is not a document. And its own totals must agree: the
/// reconciliation strip against itself, and the receipt listing against the summary, since an office reconciles the two by
/// hand and a disagreement between them would send someone looking for money that was never missing.
/// </para>
/// </summary>
public class CollectorReportTests : TestContext
{
    private static readonly Guid CollectorId = Guid.NewGuid();

    private static ReportOfCollectionsDto Sheet() => new(
        CollectorId, "Juan Dels", "EEMO-2026-001",
        new[] { FacilityCode.NPM },
        new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
        TotalCollected: 420m,
        ReceiptsIssued: 5,
        PayorsServed: 3,
        DaysWithCollections: 4,
        OfficeRecorded: 60m,
        OfficeReceipts: 2,
        Facilities: new[] { new ReportFacilityLineDto(FacilityCode.NPM, 5, 3, 420m) },
        Days: new[]
        {
            new ReportDayLineDto(new DateOnly(2026, 8, 24), "5656565 to 5656567", 3, 60m, 210m),
            new ReportDayLineDto(new DateOnly(2026, 8, 25), "2 receipts", 2, 0m, 210m)
        },
        Receipts: new[]
        {
            new ReportReceiptLineDto("5656565", new DateTime(2026, 8, 24, 19, 53, 0), "Kim Chui", "1", FacilityCode.NPM, "Aug 22 to Aug 24, 2026 (3 days)", 90m),
            new ReportReceiptLineDto("5656566", new DateTime(2026, 8, 24, 20, 2, 0), "Justin Bieber", "2", FacilityCode.NPM, "Aug 24, 2026", 120m),
            new ReportReceiptLineDto("2626261", new DateTime(2026, 8, 25, 12, 1, 0), "Karmilita Log", "7", FacilityCode.NPM, "Aug 25, 2026", 210m)
        },
        Absences: Array.Empty<ReportAbsenceLineDto>(),
        UtilityBilled: 0m,
        UtilityCollected: 0m);

    private IRenderedComponent<CollectorReport> RenderSheet(Guid? collectorId, ReportOfCollectionsDto? sheet)
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(c => c.GetReportOfCollectionsAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                  .ReturnsAsync(sheet is null
                      ? Result<ReportOfCollectionsDto>.NotFound()
                      : Result<ReportOfCollectionsDto>.Success(sheet));

        Services.AddSingleton(collectors.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IMfaApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Cly Sullano");
        auth.SetRoles("SuperAdmin");

        return collectorId is { } id
            ? RenderComponent<CollectorReport>(p => p.Add(c => c.CollectorId, id))
            : RenderComponent<CollectorReport>();
    }

    [Fact]
    public void WithoutACollectorItPrintsNoFigures()
    {
        var cut = RenderSheet(null, Sheet());

        Assert.Contains("open this report from a collector's row", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("table.print-status-table"));
    }

    [Fact]
    public void TheDailyRecordSaysWhatAnsweredForEarlierDays()
    {
        // The one column that explains a day collecting more than it could owe. Its total belongs to the sheet, not to a
        // reader's arithmetic.
        var cut = RenderSheet(CollectorId, Sheet());

        cut.WaitForAssertion(() =>
        {
            var daily = cut.FindAll("table.print-status-table")
                .First(t => (t.QuerySelector("thead")?.TextContent ?? string.Empty).Contains("For Earlier Days", StringComparison.Ordinal));
            var footer = daily.QuerySelector("tfoot tr")!.QuerySelectorAll("td");

            Assert.Equal(60m, Money(footer[^2].TextContent));    // answered for earlier days
            Assert.Equal(420m, Money(footer[^1].TextContent));   // the period's whole collection
        });
    }

    [Fact]
    public void TheReceiptListingAddsUpToTheSummary()
    {
        var cut = RenderSheet(CollectorId, Sheet());

        cut.WaitForAssertion(() =>
        {
            var summary = Figures(cut, ".print-report-summary")["Total Collected"];

            var listing = cut.FindAll("table.print-status-table")
                .First(t => (t.QuerySelector("thead")?.TextContent ?? string.Empty).Contains("OR No.", StringComparison.Ordinal));
            var rows = listing.QuerySelectorAll("tbody tr")
                .Select(r => Money(r.QuerySelectorAll("td").Last().TextContent))
                .Sum();

            Assert.Equal(summary, rows);
        });
    }

    [Fact]
    public void ARefusalIsStatedRatherThanLeftBlank()
    {
        var cut = RenderSheet(CollectorId, null);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".print-empty")));
    }

    [Fact]
    public void TheSignatoryLinesSitInTheirFooterColumns()
    {
        // The strip declares `display: contents` and hands its layout to whatever hosts it. A sheet that omits the footer
        // columns gets the lines stacked one under another down the page, which is what this document did at first.
        var cut = RenderSheet(CollectorId, Sheet());

        cut.WaitForAssertion(() =>
        {
            var footer = cut.Find(".print-report-signatures");
            Assert.NotEmpty(footer.QuerySelectorAll(".sig-slot"));
        });
    }

    [Fact]
    public void EveryPeriodTheOfficeAsksForIsOffered()
    {
        // Five, in the order the office reads them: the day's cash, the week, the month it files by, the year, and the
        // whole record. Only the last has nothing to step through.
        var cut = RenderSheet(CollectorId, Sheet());

        var tabs = cut.FindAll(".rpt-period-tab").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Daily", "Weekly", "Monthly", "Yearly", "Whole Time" }, tabs);

        cut.FindAll(".rpt-period-tab").First(t => t.TextContent.Trim() == "Whole Time").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".rpt-date-nav"));
            Assert.Contains("Every collection on record", cut.Markup, StringComparison.Ordinal);
        });
    }

    private static Dictionary<string, decimal> Figures(IRenderedComponent<CollectorReport> cut, string selector) =>
        cut.Find(selector).QuerySelectorAll("div")
            .Where(d => d.QuerySelector("span") is not null && d.QuerySelector("strong") is not null)
            .ToDictionary(
                d => d.QuerySelector("span")!.TextContent.Trim(),
                d => Money(d.QuerySelector("strong")!.TextContent),
                StringComparer.Ordinal);

    /// <summary>Reads a peso figure as written on the sheet, e.g. "₱420.00".</summary>
    private static decimal Money(string text)
    {
        var digits = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return digits.Length == 0 ? 0m : decimal.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
    }
}
