using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The ECF / WCF account registers and reports once rendered hard-coded sample accounts (fictional names, OR numbers and
/// amounts) — printable under an official letterhead. They must now show only the server's obligations, or state that
/// a figure is unavailable; never sample data, and never a document built from a failed load.
/// </summary>
public sealed class UtilityAccountsAndReportsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly string[] RetiredSampleNames = ["Juan Dela Cruz", "Pedro Santos", "Maria Flores", "Ramon Villanueva", "Shared Cleaning Area"];

    public UtilityAccountsAndReportsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EcfAccounts_ListsOnlyServerAccounts()
    {
        Services.AddSingleton(EcfApi(Ecf("Ana Reyes", "A-1", 100m, 40m, 60m), Ecf("Ben Cruz", "A-2", 50m, 50m, 0m)).Object);

        var cut = RenderComponent<ElectricityConsumptionFeesAccounts>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            Assert.Equal(2, cut.FindAll("[aria-label='Electricity accounts'] tbody tr").Count);
            Assert.Contains("Ana Reyes", cut.Markup);
            Assert.Contains("₱60.00", cut.Find("dl[aria-label='Electricity account position']").TextContent);
            AssertNoSampleData(cut);
        }, Timeout);
    }

    [Fact]
    public void WcfAccounts_ListsOnlyServerAccounts_AndStatesAFailureInsteadOfFigures()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Failure("offline"));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFeesAccounts>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Accounts are unavailable.", cut.Markup);
            Assert.DoesNotContain("₱0.00", cut.Find("dl[aria-label='Water account position']").TextContent);
            AssertNoSampleData(cut);
        }, Timeout);
    }

    [Fact]
    public void EcfReport_PrintsServerObligations_WithHonestOfficialRow()
    {
        Services.AddSingleton(EcfApi(Ecf("Ana Reyes", "A-1", 100m, 40m, 60m), Ecf("Ben Cruz", "A-2", 50m, 50m, 0m)).Object);

        var cut = RenderComponent<ElectricityConsumptionFeesReport>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            var sheet = cut.Find("article.print-report-sheet");
            Assert.Contains("Republic of the Philippines", sheet.TextContent);
            Assert.Contains("Ana Reyes", sheet.TextContent);

            var official = sheet.QuerySelector("section[aria-labelledby='ecfr-official'] tbody tr")!;
            Assert.All(official.QuerySelectorAll("td"), td => Assert.Equal("—", td.TextContent.Trim()));

            var total = sheet.QuerySelector("section[aria-labelledby='ecfr-accounts'] tfoot")!.TextContent;
            Assert.Contains("₱150.00", total);
            Assert.Contains("₱90.00", total);
            Assert.Contains("₱60.00", total);

            Assert.False(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Print").HasAttribute("disabled"));
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("download")?.EndsWith(".csv") == true);
            AssertNoSampleData(cut);
        }, Timeout);
    }

    [Fact]
    public void WcfReport_WhenObligationsFail_CannotPrintOrExport()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Failure("offline"));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFeesReport>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("can't be prepared", cut.Find("[role='alert']").TextContent);
            Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Print").HasAttribute("disabled"));
            Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Export CSV").HasAttribute("disabled"));
            Assert.DoesNotContain(cut.FindAll("a"), a => a.HasAttribute("download"));
            Assert.DoesNotContain("₱", cut.Find("section[aria-labelledby='wcfr-position']").TextContent);
            AssertNoSampleData(cut);
        }, Timeout);
    }

    private static void AssertNoSampleData<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        foreach (var name in RetiredSampleNames)
            Assert.DoesNotContain(name, cut.Markup);
        Assert.DoesNotContain("001234567", cut.Markup);
    }

    private static Mock<IEcfCollectionsApiClient> EcfApi(params EcfObligationQuoteDto[] quotes)
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(quotes));
        return api;
    }

    private static EcfObligationQuoteDto Ecf(string payer, string stall, decimal assessed, decimal settled, decimal outstanding) => new(
        Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 1,
        Guid.NewGuid(), stall, "Tenant Market", "Dry Goods", 2026, 9, 100m, 110m, 10m, 10m,
        assessed, settled, outstanding, SettlementAuthority.Canonical, null, payer,
        Guid.NewGuid(), Guid.NewGuid(), "ECF", RevenueInstrumentType.OfficialReceipt, "Metered", true, true);
}
