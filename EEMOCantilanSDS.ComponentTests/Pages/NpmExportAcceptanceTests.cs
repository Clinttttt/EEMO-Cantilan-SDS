using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit.Abstractions;
using ExportPage = EEMOCantilanSDS.Client.Components.Pages.Reports.ExportData;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Production component and CSV/print markup, with synthetic DTOs; no HTTP service or database.</summary>
public sealed class NpmExportAcceptanceTests(ITestOutputHelper output) : TestContext
{
    [Theory]
    [InlineData("october-as-known", 0)]
    [InlineData("october-latest-corrected", 1)]
    public void Thousand_row_CSV_and_print_share_exact_supplied_totals_without_fabricated_receipts(string basis, int reversed)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Synthetic Head");
        var rows = Enumerable.Range(1, 1000).Select(i => new CollectionRentalRowDto(
            "EXPORT-" + i.ToString("D4"), i % 3 == 0 ? "Fish Area" : i % 3 == 1 ? "Vegetable Area" : "Meat Area",
            "Synthetic Vendor " + i.ToString("D4"), i % 5 == 0 ? 0 : 35,
            "Partial", i == 1 && reversed == 1 ? 0 : 875, i == 1 && reversed == 1 ? 900 : 25,
            null, 900, i == 1 && reversed == 1 ? 900 : 25)).ToArray();
        var dto = new CollectionReportDto("October 2026", new(2026, 10, 31),
            [new(FacilityCode.NPM, "Synthetic market", "Monthly rent", true,
                rows.Sum(x => x.Collected), rows.Sum(x => x.Balance), rows, [])]);
        var api = new Mock<IReportsApiClient>();
        api.Setup(x => x.GetCollectionReportAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<CollectionReportDto>.Success(dto));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>()); Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>()); Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(FacilityCatalogFixture.NamingTheMarket("Synthetic market", "NPM", "Vegetable Area", "Fish Area", "Meat Area"));
        var brand = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());
        brand.Apply(new("SYNTHETIC", "synthetic", "Synthetic Acceptance LGU", "Test Province",
            "SYNTHETIC TEST OFFICE - NOT AN OFFICIAL REPORT", null, "Active", true, ReportSignatories: "[]"));
        Services.AddSingleton(brand);
        var allocatedBefore = GC.GetTotalAllocatedBytes(); var watch = Stopwatch.StartNew();
        var cut = RenderComponent<ExportPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".ex-sheet")), TimeSpan.FromSeconds(15));
        var csv = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!["data:text/csv;charset=utf-8,".Length..]);
        var csvRows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line => line.TrimEnd('\r').Split(',').Select(x => x.Trim('"')).ToArray()).ToArray();
        Assert.Equal(1000, csvRows.Length);
        Assert.Equal(rows.Sum(x => x.Collected), csvRows.Sum(x => decimal.Parse(x[7], CultureInfo.InvariantCulture)));
        Assert.Equal(rows.Sum(x => x.Balance), csvRows.Sum(x => decimal.Parse(x[8], CultureInfo.InvariantCulture)));
        Assert.All(csvRows, row => Assert.True(string.IsNullOrEmpty(row[11]), "Summary must not invent an OR/SRC."));
        var printed = cut.Find(".ex-table tfoot").TextContent;
        Assert.Contains(rows.Sum(x => x.Collected).ToString("N2"), printed);
        Assert.Contains(rows.Sum(x => x.Balance).ToString("N2"), printed);
        Assert.Equal(1000, cut.FindAll(".ex-table tbody tr").Count(r => r.QuerySelectorAll("td").Length == 9));
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Print").Click();
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Save as PDF").Click();
        Assert.Equal(2, JSInterop.Invocations.Count(x => x.Identifier == "stalltrackPrint.landscape"));
        watch.Stop(); var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var directory = Path.Combine(Path.GetTempPath(), "stalltrack-npm-acceptance"); Directory.CreateDirectory(directory);
        var css = File.ReadAllText(Path.Combine(root, "EEMOCantilanSDS.Client/Components/Pages/Reports/ExportData.razor.css"));
        var html = "<!doctype html><meta charset='utf-8'><title>Synthetic acceptance</title><style>" + css +
            ":root{--navy:#173b61;--bg:white;--text:#172b42;--text-muted:#536477;--border:#cfd6de;}body{font-family:Arial,sans-serif;}" +
            "@page{size:A4 landscape;margin:0;}html,body{margin:0!important}</style>" + cut.Markup;
        File.WriteAllText(Path.Combine(directory, basis + ".html"), html);
        File.WriteAllText(Path.Combine(directory, basis + ".csv"), csv);
        File.WriteAllText(Path.Combine(directory, basis + ".json"), JsonSerializer.Serialize(new {
            basis, rows = rows.Length, collected = rows.Sum(x => x.Collected), outstanding = rows.Sum(x => x.Balance),
            renderAndCsvMs = watch.ElapsedMilliseconds, allocatedBytes = allocated, authenticated = false,
            source = "production Blazor component with synthetic DTOs; not an official RCD" }));
        output.WriteLine($"{basis}: 1000 rows; render/CSV ms={watch.ElapsedMilliseconds}; allocated bytes={allocated}");
    }
}
