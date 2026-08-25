using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using CollectorReport = EEMOCantilanSDS.Client.Components.Pages.Reports.CollectorReport;

/// <summary>
/// The Report of Collections while it is still a MOCK of the layout.
///
/// <para>
/// Two things about a sample document are worth a test rather than a comment. It must say on its face that its figures
/// are not office records, and it must say so on paper as well as on screen, because a printed sample that looks
/// official is worse than no sample at all. And the reconciliation strip has to add up: collected less remitted is what
/// the collector still holds, and a document that states the three figures must not state them inconsistently.
/// </para>
/// </summary>
public class CollectorReportMockTests : TestContext
{
    private IRenderedComponent<CollectorReport> RenderReport()
    {
        // The shared _Imports injects these into every component; stub them so the page resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IMfaApiClient>());
        // SignatureStrip reads the office's saved signatories.
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Cly Sullano");
        auth.SetRoles("SuperAdmin");

        return RenderComponent<CollectorReport>();
    }

    [Fact]
    public void TheSheetSaysItsFiguresAreNotOfficeRecords()
    {
        var cut = RenderReport();

        var note = cut.Find(".cr-sample-note").TextContent;
        Assert.Contains("Sample layout", note, StringComparison.Ordinal);
        Assert.Contains("not an office record", note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoticeIsInsideTheSheetSoItPrintsWithIt()
    {
        // Anything marked no-print, or sitting in the topbar, is absent from the paper. The notice has to be part of
        // the document itself.
        var cut = RenderReport();

        var sheet = cut.Find("section.print-report-sheet");
        var note = sheet.QuerySelector(".cr-sample-note");

        Assert.NotNull(note);
        Assert.DoesNotContain("no-print", note!.ClassName ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReconciliationStripAddsUp()
    {
        var cut = RenderReport();

        var strip = cut.Find(".cr-reconcile");
        var figures = strip.QuerySelectorAll("div")
            .Select(d => new
            {
                Key = d.QuerySelector("span")!.TextContent.Trim(),
                Value = Money(d.QuerySelector("strong")!.TextContent)
            })
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        Assert.Equal(
            figures["Total Collected"] - figures["Total Remitted"],
            figures["Not Yet Remitted"]);
    }

    [Fact]
    public void TheRemittanceTableTotalIsTheSumOfItsRows()
    {
        var cut = RenderReport();

        var table = cut.FindAll("table.print-status-table")
            .First(t => (t.QuerySelector("thead")?.TextContent ?? string.Empty)
                .Contains("Received By", StringComparison.Ordinal));

        var rows = table.QuerySelectorAll("tbody tr")
            .Select(r => Money(r.QuerySelectorAll("td").Last().TextContent))
            .ToList();

        var footer = Money(table.QuerySelector("tfoot tr")!.QuerySelectorAll("td").Last().TextContent);

        Assert.Equal(rows.Sum(), footer);
    }

    /// <summary>Reads a peso figure as written on the sheet, e.g. "₱7,410.00".</summary>
    private static decimal Money(string text) =>
        decimal.Parse(
            new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
}
