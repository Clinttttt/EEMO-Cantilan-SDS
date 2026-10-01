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
        // Operation reports read the official Monthly Income and the governed-activity register; an unanswered read
        // shows "—", which is what these tests assert.
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton(Mock.Of<IGovernedServicesApiClient>());
        Services.AddSingleton(Mock.Of<ICollectorsApiClient>());
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

    // ── WCF Head/Admin source entry (IA-053): the office's own approved amount, then activation with attestation ──

    private static WcfSetupSourceDto Source(decimal? amount, SettlementAuthority? authority, bool editable = true) =>
        new(Guid.NewGuid(), "12", "Fish", "Bobby Example", amount is null ? null : Guid.NewGuid(), amount,
            amount is null ? null : "DirectApproved", authority, 0m, editable, editable ? null : "Frozen");

    private Mock<IWcfCollectionsApiClient> SetupApi(params WcfSetupSourceDto[] sources)
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Success([]));
        api.Setup(x => x.GetSetupSourcesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfSetupSourceDto>>.Success(sources));
        Services.AddSingleton(api.Object);
        return api;
    }

    [Fact]
    public void WcfAccounts_LetsTheOfficeSetTheApprovedWaterAmount_WithNoMeterFields()
    {
        var source = Source(null, null);
        var api = SetupApi(source);
        api.Setup(x => x.EstablishObligationAsync(It.IsAny<WcfObligationSetupRequest>()))
            .ReturnsAsync(Result<WcfSetupSourceDto>.Success(source with { ApprovedAmount = 10m }));

        var cut = RenderComponent<WaterConsumptionFeesAccounts>();

        cut.WaitForAssertion(() => Assert.Contains("Bobby Example", cut.Find("section.wcfs").TextContent), Timeout);
        var panel = cut.Find("section.wcfs").TextContent;
        Assert.Contains("No amount set", panel);
        foreach (var meter in new[] { "reading", "cubic", "m³", "Meter", "rate per" })
            Assert.DoesNotContain(meter, panel, StringComparison.OrdinalIgnoreCase);

        cut.Find("section.wcfs input[type='number']").Change("10");
        cut.FindAll("section.wcfs button").Single(b => b.TextContent.Trim() == "Save").Click();

        // The amount is the office's, sent as typed, for the stall and month on screen.
        api.Verify(x => x.EstablishObligationAsync(It.Is<WcfObligationSetupRequest>(r =>
            r.StallId == source.StallId && r.ApprovedAmount == 10m)), Times.Once);
    }

    [Fact]
    public void WcfAccounts_RoutineRowsNeedNoActivation_AndOnlyHistoricalRowsOfferTheLegacyMigration()
    {
        var notSet = Source(null, null);
        var prepared = Source(10m, SettlementAuthority.Legacy) with { StallNo = "13", PayerName = "Ana Reyes" };
        var historical = Source(10m, SettlementAuthority.Legacy, editable: false) with { StallNo = "15", PayerName = "Cora Lim" };
        var active = Source(10m, SettlementAuthority.Canonical, editable: false) with { StallNo = "14", PayerName = "Ben Cruz" };
        var api = SetupApi(notSet, prepared, historical, active);
        api.Setup(x => x.GetActivationReadinessAsync(historical.UtilityBillId!.Value, null))
            .ReturnsAsync(Result<SettlementCutoverReadinessDto>.Failure("not checked"));

        var cut = RenderComponent<WaterConsumptionFeesAccounts>();

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("section.wcfs tbody tr").Count), Timeout);
        var rows = cut.FindAll("section.wcfs tbody tr");
        // No per-row activation for routine rows: a prepared amount is simply ready for Collector Mobile.
        Assert.DoesNotContain("Activate for Mobile", cut.Markup);
        Assert.Contains("Prepared", rows[1].TextContent);
        Assert.DoesNotContain("Review legacy migration", rows[1].TextContent);
        Assert.Contains("Active", rows[3].TextContent);
        Assert.DoesNotContain("Review legacy migration", rows[3].TextContent);
        Assert.Contains("Historical record", rows[2].TextContent);

        // Only the historical row opens the attested migration review, under its own name.
        rows[2].QuerySelectorAll("button").Single(b => b.TextContent.Contains("Review legacy migration")).Click();
        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[role='dialog']");
            Assert.Contains("Review legacy WCF migration", dialog.TextContent);
            Assert.Contains("Office attestation", dialog.TextContent);
            api.Verify(x => x.ActivateAsync(It.IsAny<EEMOCantilanSDS.Application.Common.Revenue.WcfActivationRequest>()), Times.Never);
        }, Timeout);
    }
}
