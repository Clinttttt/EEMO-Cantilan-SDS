using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>ECF and WCF own their statements of account: read from their own obligation reads, with no NPM dependency.</summary>
public sealed class UtilityStatementsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid Stall = Guid.NewGuid();

    public UtilityStatementsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    [Fact]
    public void Ecf_StatementsLoadFromTheEcfRead_AsTheEightColumnRegister_AndNeedNoNpmView()
    {
        Assert.Equal("/operations/ecf/statements",
            Assert.Single(typeof(ElectricityConsumptionFeesStatements).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<EcfObligationQuoteDto>>.Success([Quote("Pedro Vendor", 650m, 150m, 500m)]));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<ElectricityConsumptionFeesStatements>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["#", "Name of Lessee (Actual Occupants)", "Billing Month", "Operations", "Section", "Stall No.", "Assessed", "Balance"],
                cut.FindAll(".lsh-table thead th").Select(t => string.Join(' ', t.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray());
            var row = Assert.Single(cut.FindAll(".lsh-table tbody tr"));
            Assert.Equal(8, row.Children.Length);
            Assert.Contains("Pedro Vendor", row.TextContent);
            Assert.Contains("October 2026", row.TextContent);
            Assert.Contains("₱650.00", row.TextContent);
            Assert.Contains("₱500.00", row.TextContent);
            Assert.DoesNotContain("Net settled", cut.Markup);                              // no settlement/status column in the official register
            Assert.NotNull(cut.Find(".lsh-actions a[download]"));
        }, Timeout);
    }

    [Fact]
    public void Wcf_BalanceOnlyFilterHidesSettledLines_AndTheCsvFollowsTheFilteredRows()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<WcfObligationQuoteDto>>.Success([WcfQuote("Ana Stall", 100m, 100m, 0m), WcfQuote("Ben Stall", 100m, 0m, 100m)]));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFeesStatements>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".lsh-table tbody tr").Count), Timeout);

        cut.FindAll(".lsh-seg-item").Single(b => b.TextContent.Contains("With balance")).Click();
        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll(".lsh-table tbody tr"));
            Assert.Contains("Ben Stall", row.TextContent);
            var csv = Uri.UnescapeDataString(cut.Find(".lsh-actions a[download]").GetAttribute("href")!);
            Assert.Contains("Ben Stall", csv);
            Assert.DoesNotContain("Ana Stall", csv);
        }, Timeout);
    }

    [Fact]
    public void Ecf_AnOfficeThatHasNotFinishedSetup_SeesSetupRequiredWithTheServersReason_NotAGenericLoadFailure()
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<EcfObligationQuoteDto>>.Failure("ECF has no active revenue classification for this tenant.", 409));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<ElectricityConsumptionFeesStatements>();

        cut.WaitForAssertion(() =>
        {
            var notice = cut.Find("[role=alert]");
            Assert.Contains("Setup required", notice.TextContent);
            Assert.Contains("ECF has no active revenue classification for this tenant.", notice.TextContent);
            Assert.Equal("/settings/revenue", notice.QuerySelector("a")!.GetAttribute("href"));
            Assert.DoesNotContain("be loaded", cut.Markup);
            Assert.Empty(cut.FindAll("button:contains(\"Try again\")"));
        }, Timeout);
    }

    [Fact]
    public void Wcf_AnOfficeThatHasNotFinishedSetup_SeesSetupRequired_AndATemporaryFailureStillOffersRetry()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.SetupSequence(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Failure("WCF has no active revenue classification for this tenant.", 409))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Failure("down", 503));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFeesStatements>();
        cut.WaitForAssertion(() => Assert.Contains("Setup required", cut.Find("[role=alert]").TextContent), Timeout);
        Assert.Empty(cut.FindAll("[role=alert] button"));

        // A month change reloads; a 503 is a temporary failure, not setup: generic wording with an explicit retry.
        cut.Find("input[type=month], input[type=date]").Change("2026-09");
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Setup required", cut.Find("[role=alert]").TextContent);
            Assert.Single(cut.FindAll("[role=alert] button"));
        }, Timeout);
    }

    private static EcfObligationQuoteDto Quote(string payor, decimal assessed, decimal settled, decimal outstanding) => new(
        Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 1, Stall, "A-1", "NPM", "Dry Goods",
        2026, 10, 0m, 0m, 0m, 0m, assessed, settled, outstanding, SettlementAuthority.Legacy, null, payor,
        Guid.NewGuid(), Guid.NewGuid(), "ECF", RevenueInstrumentType.OfficialReceipt, "Direct", true, true);

    private static WcfObligationQuoteDto WcfQuote(string payor, decimal assessed, decimal settled, decimal outstanding) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "B-" + payor[..1], "NPM", "Fish", 2026, 10, 0m, 0m, 0m, 0m, assessed, settled,
        outstanding, SettlementAuthority.Legacy, null, payor, Guid.NewGuid(), Guid.NewGuid(), "WCF", RevenueInstrumentType.CashTicket, "Direct", true);
}
