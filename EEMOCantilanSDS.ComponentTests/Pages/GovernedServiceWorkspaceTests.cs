using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Market Fees, Landing / Berthing, Transfer Large Cattle and Vegetable / Fruits are governed configurable services
/// (IA-044): the Head records the approved amount rule, collectors collect on Mobile, and these workspaces show the
/// server's setup and the collections actually posted. They record no money themselves.
/// </summary>
public sealed class GovernedServiceWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IGovernedServicesApiClient> _api = new();

    public GovernedServiceWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(_api.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private void Serve(IReadOnlyList<GovernedServiceDefinitionDto> definitions, IReadOnlyList<GovernedServiceActivityDto>? activity = null)
    {
        _api.Setup(x => x.GetDefinitionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceDefinitionDto>>.Success(definitions));
        _api.Setup(x => x.GetActivityAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success(activity ?? []));
    }

    private static GovernedServiceDefinitionDto Definition(
        string code, string name, GovernedServiceSetupState state, GovernedServiceBasis? basis = null,
        decimal? fixedAmount = null, decimal? ceiling = null, bool mobile = false, bool modeAware = false,
        RevenueInstrumentType instrument = RevenueInstrumentType.CashTicket, params string[] issues) => new(
        code, name, code, modeAware,
        modeAware ? [GovernedServiceBasis.DirectApprovedAmount] : [GovernedServiceBasis.FixedAmount, GovernedServiceBasis.DirectApprovedAmount],
        state, basis, fixedAmount, ceiling, mobile, basis is null ? null : new DateOnly(2026, 9, 1),
        modeAware
            ? [new(GovernedServiceMode.WholePayment, RevenueInstrumentType.OfficialReceipt, "whole"),
               new(GovernedServiceMode.DailyTransaction, RevenueInstrumentType.CashTicket, "daily")]
            : [new(null, instrument, "policy")],
        issues);

    private static IReadOnlyList<GovernedServiceDefinitionDto> All(GovernedServiceSetupState state = GovernedServiceSetupState.SetupRequired) =>
    [
        Definition(CollectorOperationCodes.MarketFees, "Market Fees", state, issues: state == GovernedServiceSetupState.SetupRequired ? ["The amount rule has not been set up."] : []),
        Definition(CollectorOperationCodes.LandingBerthing, "Landing / Berthing", state),
        Definition(CollectorOperationCodes.TransferLargeCattle, "Transfer Large Cattle", state, instrument: RevenueInstrumentType.OfficialReceipt),
        Definition(CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental", state, modeAware: true),
    ];

    private static GovernedServiceActivityDto Row(decimal amount, string document = "CT000010", GovernedServiceMode? mode = null,
        RevenueInstrumentType instrument = RevenueInstrumentType.CashTicket) => new(
        Guid.NewGuid(), new DateOnly(2026, 9, 30), DateTime.UtcNow, document, instrument, mode, "Maria Santos", "Stall 4",
        amount, "Ana Reyes", "Posted");

    [Theory]
    [InlineData(typeof(MarketFees), "/operations/market-fees", "Market Fees")]
    [InlineData(typeof(LandingBerthing), "/operations/landing-berthing", "Landing / Berthing")]
    [InlineData(typeof(TransferLargeCattle), "/operations/transfer-large-cattle", "Transfer Large Cattle")]
    [InlineData(typeof(VegetableFruit), "/operations/vegetable-fruit", "Vegetable / Fruits")]
    public void Workspace_IsOfficeOnly_ShowsSetupAndCollections_AndRecordsNothing(Type page, string route, string title)
    {
        Assert.Equal(route, Assert.Single(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(page.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Serve(All());

        var cut = Render(b => { b.OpenComponent(0, page); b.CloseComponent(); });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(title, Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            Assert.NotEmpty(cut.FindAll("table"));
            Assert.Contains("Setup required", cut.Find("aside").TextContent);
            // Stale wording must not survive now that a writer exists.
            Assert.DoesNotContain("aren't recorded in StallTrack yet", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record") || b.TextContent.Contains("Collect"));
        }, Timeout);
    }

    [Fact]
    public void SetupRequired_SaysWhatIsMissing_AndSaysNothingWasCollected()
    {
        Serve(All());

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Not set up", cut.Find("aside").TextContent);
            Assert.Contains("The amount rule has not been set up.", cut.Find("aside").TextContent);
            Assert.Contains("Nothing has been collected: Market Fees is not active yet.", cut.Markup);
            Assert.DoesNotContain("₱", cut.Find("table").TextContent);
        }, Timeout);
    }

    [Fact]
    public void ActiveService_ListsPostedCollections_WithDocumentPayerCollectorAndTotal()
    {
        Serve([
                Definition(CollectorOperationCodes.MarketFees, "Market Fees", GovernedServiceSetupState.Active,
                    GovernedServiceBasis.FixedAmount, fixedAmount: 30m, mobile: true)
            ],
            [Row(30m, "CT000010"), Row(30m, "CT000011")]);

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("CT000010", rows[0].TextContent);
            Assert.Contains("Maria Santos", rows[0].TextContent);
            Assert.Contains("Stall 4", rows[0].TextContent);
            Assert.Contains("Ana Reyes", rows[0].TextContent);
            Assert.Contains("₱60.00", cut.Find("tfoot").TextContent);
            var setup = cut.Find("aside").TextContent;
            Assert.Contains("Active", setup);
            Assert.Contains("Fixed · ₱30.00", setup);
            Assert.Contains("Enabled", setup);
        }, Timeout);
    }

    [Fact]
    public void Vegetable_ShowsTheInstrumentResolvedByEachMode_AndDailyRowsShowTheirMode()
    {
        Serve(All(GovernedServiceSetupState.Active),
            [Row(300m, "OR000005", GovernedServiceMode.WholePayment, RevenueInstrumentType.OfficialReceipt),
             Row(20m, "CT000006", GovernedServiceMode.DailyTransaction)]);

        var cut = RenderComponent<VegetableFruit>();

        cut.WaitForAssertion(() =>
        {
            var setup = cut.Find("aside").TextContent;
            Assert.Contains("Whole payment", setup);
            Assert.Contains("Official Receipt", setup);
            Assert.Contains("Daily transaction", setup);
            Assert.Contains("Cash Ticket", setup);
            var rows = cut.FindAll("tbody tr");
            Assert.Contains("Official Receipt · Whole payment", rows[0].TextContent);
            Assert.Contains("Cash Ticket · Daily transaction", rows[1].TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheHeadSavesAnAmountRule_AsANewVersion_AndTheWorkspaceUpdates()
    {
        Serve(All());
        ConfigureGovernedServiceRequest? sent = null;
        _api.Setup(x => x.ConfigureAsync(CollectorOperationCodes.MarketFees, It.IsAny<ConfigureGovernedServiceRequest>()))
            .Callback<string, ConfigureGovernedServiceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<GovernedServiceDefinitionDto>.Success(Definition(
                CollectorOperationCodes.MarketFees, "Market Fees", GovernedServiceSetupState.Active,
                GovernedServiceBasis.FixedAmount, fixedAmount: 25m, mobile: true)));

        var cut = RenderComponent<MarketFees>();
        cut.WaitForAssertion(() => cut.Find("aside button.gsw-edit"), Timeout);
        cut.Find("aside button.gsw-edit").Click();

        cut.Find("aside select").Change(GovernedServiceBasis.FixedAmount.ToString());
        cut.Find("aside input[type='number']").Change("25");
        cut.Find("aside form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(GovernedServiceBasis.FixedAmount, sent!.Basis);
            Assert.Equal(25m, sent.FixedAmount);
            Assert.Null(sent.MaximumAmount);
            Assert.True(sent.IsEnabled);
            Assert.Contains("Amount rule saved", cut.Markup);
            Assert.Contains("Fixed · ₱25.00", cut.Find("aside").TextContent);
        }, Timeout);
    }

    [Fact]
    public void AnAdmin_CanReadTheSetup_ButHasNoWayToChangeIt()
    {
        var context = this.AddTestAuthorization();
        context.SetAuthorized("admin").SetRoles("Admin");
        Serve(All());

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Setup required", cut.Find("aside").TextContent);
            Assert.Empty(cut.FindAll("button.gsw-edit"));
            Assert.Empty(cut.FindAll("aside form"));
        }, Timeout);
    }

    [Fact]
    public void FailedSetupOrCollections_SayTheyCouldNotBeLoaded_InsteadOfShowingNothing()
    {
        _api.Setup(x => x.GetDefinitionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceDefinitionDto>>.Failure("offline"));
        _api.Setup(x => x.GetActivityAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Failure("offline"));

        var cut = RenderComponent<LandingBerthing>();

        cut.WaitForAssertion(() =>
        {
            var alerts = cut.FindAll("[role='alert']").Select(x => x.TextContent).ToList();
            Assert.Contains(alerts, a => a.Contains("setup couldn't be loaded"));
            Assert.Contains(alerts, a => a.Contains("Collections couldn't be loaded"));
            Assert.Contains("Collections are unavailable.", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void ChangingTheMonth_ReloadsTheActivityForThatMonthOnly()
    {
        Serve(All(GovernedServiceSetupState.Active));

        var cut = RenderComponent<MarketFees>();
        cut.WaitForAssertion(() => cut.Find("input[type='month']"), Timeout);
        cut.Find("input[type='month']").Change("2026-07");

        cut.WaitForAssertion(() => _api.Verify(x => x.GetActivityAsync(
            CollectorOperationCodes.MarketFees, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), Times.Once), Timeout);
        _api.Verify(x => x.GetDefinitionsAsync(), Times.Once); // the setup is not re-fetched per month
    }
}
