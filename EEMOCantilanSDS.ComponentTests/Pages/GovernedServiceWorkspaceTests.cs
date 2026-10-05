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

    private static GovernedServiceActivityDto Row(decimal amount, string document = "SRC-2026-000010", GovernedServiceMode? mode = null,
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
    public void Vegetable_ShowsTheInstrumentPolicyResolvedByEachMode_AndRowsShowTheirSrcAndMode()
    {
        Serve(All(GovernedServiceSetupState.Active),
            [Row(300m, "SRC-2026-000005", GovernedServiceMode.WholePayment, RevenueInstrumentType.OfficialReceipt),
             Row(20m, "SRC-2026-000006", GovernedServiceMode.DailyTransaction)]);

        var cut = RenderComponent<VegetableFruit>();

        cut.WaitForAssertion(() =>
        {
            var setup = cut.Find("aside").TextContent;
            Assert.Contains("Monthly rental", setup);
            Assert.Contains("Official Receipt", setup);
            Assert.Contains("Daily transaction", setup);
            Assert.Contains("Cash Ticket", setup);
            var rows = cut.FindAll("tbody tr");
            Assert.Contains("SRC-2026-000005", rows[0].TextContent);
            Assert.Contains("Monthly rental", rows[0].TextContent);
            Assert.Contains("SRC-2026-000006", rows[1].TextContent);
            Assert.Contains("Daily transaction", rows[1].TextContent);
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

        cut.Find("[role='dialog'] select").Change(GovernedServiceBasis.FixedAmount.ToString());
        cut.Find("[role='dialog'] input[type='number']").Change("25");
        cut.Find("[role=dialog] form").Submit();

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

    private GovernedServiceDefinitionDto FeeTypeDefinition() =>
        Definition(CollectorOperationCodes.MarketFees, "Market Fees", GovernedServiceSetupState.Active,
            GovernedServiceBasis.ApprovedFeeOption, mobile: true) with
        {
            AllowedBases = [GovernedServiceBasis.FixedAmount, GovernedServiceBasis.DirectApprovedAmount, GovernedServiceBasis.ApprovedFeeOption]
        };

    private static GovernedServiceFeeOptionDto Option(string name, string? code, string? location, GovernedServiceBasis? basis,
        decimal? amount, decimal? ceiling, string status) => new(
        Guid.NewGuid(), code, name, location, null, basis, amount, ceiling, basis is null ? null : new DateOnly(2026, 9, 1), status,
        status == "Retired" ? new DateOnly(2026, 9, 15) : null,
        basis is null ? [] : [new FeeOptionRateVersionDto(new DateOnly(2026, 9, 1), basis.Value, amount, ceiling, "head", DateTime.UtcNow)]);

    [Fact]
    public void Collections_ScrollInsideABoundedRegion_TheHelperSentenceIsGone_AndTheSetupDrawerLeavesThePageAlone()
    {
        Serve(All(GovernedServiceSetupState.Active), Enumerable.Range(1, 7).Select(i => Row(10m * i, $"SRC-2026-0000{i:00}")).ToArray());

        var cut = RenderComponent<LandingBerthing>();

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find(".gsw-scroll");                                                         // about four rows show; the rest scroll here
            Assert.Equal(7, region.QuerySelectorAll("tbody tr").Length);
            Assert.NotNull(region.QuerySelector("thead th"));
            Assert.NotNull(region.QuerySelector("tfoot"));
            Assert.DoesNotContain("Recorded by assigned collectors", cut.Markup);
            Assert.DoesNotContain("Assign collectors", cut.Markup);
        }, Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Change amount rule").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.Contains("Change amount rule", drawer.QuerySelector(".eemo-drawer-header-title")!.TextContent);
            Assert.NotNull(drawer.QuerySelector("form"));
            Assert.Empty(cut.Find("aside").QuerySelectorAll("form"));                                     // nothing stretches the Setup panel
            Assert.Equal(7, cut.Find(".gsw-scroll").QuerySelectorAll("tbody tr").Length);                  // and the collections stay where they are
        }, Timeout);
        cut.FindAll("footer button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")), Timeout);
    }

    [Fact]
    public void LandingBerthing_ListsItsFeeTypes_ByTheServersOwnDefinitions_WhenTheyExist()
    {
        Serve([Definition(CollectorOperationCodes.LandingBerthing, "Landing / Berthing", GovernedServiceSetupState.Active,
                GovernedServiceBasis.ApprovedFeeOption, mobile: true) with
            { AllowedBases = [GovernedServiceBasis.FixedAmount, GovernedServiceBasis.ApprovedFeeOption] }]);
        _api.Setup(x => x.GetFeeOptionsAsync(CollectorOperationCodes.LandingBerthing)).ReturnsAsync(
            Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([
                Option("Landing", null, null, GovernedServiceBasis.FixedAmount, 100m, null, "Active"),
                Option("Berthing", null, null, GovernedServiceBasis.FixedAmount, 250m, null, "Active")]));
        _api.Setup(x => x.GetFeeOptionTotalsAsync(CollectorOperationCodes.LandingBerthing, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success([]));

        var cut = RenderComponent<LandingBerthing>();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("[aria-label='Landing / Berthing fees']");
            Assert.Equal(new[] { "Landing", "Berthing" }, panel.QuerySelectorAll("tbody tr td.v3-strong").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Contains("₱100.00", panel.TextContent);
            Assert.Contains("₱250.00", panel.TextContent);
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Add fee type");
        }, Timeout);
    }

    [Fact]
    public void MarketFeeDefinitions_ListNameRuleAmountAndStatus_FromServerData_WithoutInternalCodes()
    {
        var comfort = Option("Comfort Room", "CR_TERMINAL", "Transport Terminal", GovernedServiceBasis.FixedAmount, 5m, null, "Active");
        var parking = Option("Overnight Parking", null, null, GovernedServiceBasis.DirectApprovedAmount, null, 100m, "Active");
        var retired = Option("Old Sweeping Fee", "OLD_SWEEP", null, GovernedServiceBasis.FixedAmount, 10m, null, "Retired");
        Serve([FeeTypeDefinition()]);
        _api.Setup(x => x.GetFeeOptionsAsync(CollectorOperationCodes.MarketFees))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([comfort, parking, retired]));
        _api.Setup(x => x.GetFeeOptionTotalsAsync(CollectorOperationCodes.MarketFees, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success([
                new FeeOptionTotalDto(comfort.Id, "Comfort Room — Transport Terminal", 4, 20m),
                new FeeOptionTotalDto(parking.Id, "Overnight Parking", 1, 40m)]));

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.FindAll("section").Single(s => s.TextContent.Contains("Market Fee definitions"));
            var headers = panel.QuerySelectorAll("table")[0].QuerySelectorAll("thead th").Select(h => h.TextContent.Trim()).ToList();
            Assert.Equal(new[] { "Name", "Rule", "Amount", "Status" }, headers.Take(4));
            var rows = panel.QuerySelectorAll("table")[0].QuerySelectorAll("tbody tr");
            Assert.Equal(3, rows.Length);
            Assert.Contains("Comfort Room", rows[0].TextContent);
            Assert.Contains("Transport Terminal", rows[0].TextContent);
            Assert.Contains("Fixed", rows[0].TextContent);
            Assert.Contains("₱5.00", rows[0].TextContent);
            Assert.Contains("Up to ₱100.00", rows[1].TextContent);
            Assert.Contains("Retired", rows[2].TextContent);
            Assert.DoesNotContain("CR_TERMINAL", cut.Markup);
            Assert.DoesNotContain("OLD_SWEEP", cut.Markup);
            // A retired option keeps its history but offers no change.
            Assert.DoesNotContain(rows[2].QuerySelectorAll("button"), b => b.TextContent.Contains("Retire") || b.TextContent.Contains("Schedule"));
            // Drill-down by fee type adds up to the service's total.
            Assert.Contains("Collected by fee type", panel.TextContent);
            Assert.Contains("₱60.00", panel.QuerySelector(".fod-totals tfoot")!.TextContent);
            Assert.Contains("By approved fee type", cut.Find("aside").TextContent);
        }, Timeout);

        cut.FindAll("button").First(b => b.TextContent.Trim() == "History").Click();
        cut.WaitForAssertion(() => Assert.Contains("Set by head", cut.Markup), Timeout);
    }

    [Fact]
    public void MarketFeeDefinitions_AreReadOnlyForAdmin()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        Serve([FeeTypeDefinition()]);
        _api.Setup(x => x.GetFeeOptionsAsync(CollectorOperationCodes.MarketFees))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([
                Option("Comfort Room", null, null, GovernedServiceBasis.FixedAmount, 5m, null, "Active")]));
        _api.Setup(x => x.GetFeeOptionTotalsAsync(CollectorOperationCodes.MarketFees, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success([]));

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Comfort Room", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Add fee type")
                || b.TextContent.Contains("Schedule amount") || b.TextContent.Trim() == "Retire");
        }, Timeout);
    }

    [Fact]
    public void ADisabledService_SaysWhyMobileIsNotCollecting_AndCanBeEnabledInOneClick()
    {
        var disabled = Definition(CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental",
            GovernedServiceSetupState.Disabled, GovernedServiceBasis.DirectApprovedAmount, ceiling: 1000m, mobile: true, modeAware: true);
        Serve([disabled]);
        ConfigureGovernedServiceRequest? sent = null;
        _api.Setup(x => x.ConfigureAsync(CollectorOperationCodes.VegetableFruitSpaceRental, It.IsAny<ConfigureGovernedServiceRequest>()))
            .Callback<string, ConfigureGovernedServiceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<GovernedServiceDefinitionDto>.Success(disabled with { State = GovernedServiceSetupState.Active }));

        var cut = RenderComponent<VegetableFruit>();

        cut.WaitForAssertion(() =>
        {
            var setup = cut.Find("aside").TextContent;
            Assert.Contains("Allowed, but new transactions are not enabled", setup);   // never a bare "Enabled" next to Disabled
            Assert.DoesNotContain("Whole payment", cut.Markup);
            Assert.Contains("Monthly rental", cut.Markup);
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Enable new transactions").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.True(sent!.IsEnabled && sent.MobileEnabled);
            Assert.Equal((GovernedServiceBasis.DirectApprovedAmount, (decimal?)1000m), (sent.Basis, sent.MaximumAmount));   // same approved rule
            Assert.Contains("Active", cut.Find("aside").TextContent);
        }, Timeout);
    }

    [Fact]
    public void FeeTypes_AreOnlyOfferedToCollectorsUnderTheFeeTypeRule_AndTheHeadCanSwitchToIt()
    {
        var direct = FeeTypeDefinition() with { Basis = GovernedServiceBasis.DirectApprovedAmount };
        Serve([direct]);
        _api.Setup(x => x.GetFeeOptionsAsync(CollectorOperationCodes.MarketFees))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([]));
        _api.Setup(x => x.GetFeeOptionTotalsAsync(CollectorOperationCodes.MarketFees, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success([]));
        ConfigureGovernedServiceRequest? sent = null;
        _api.Setup(x => x.ConfigureAsync(CollectorOperationCodes.MarketFees, It.IsAny<ConfigureGovernedServiceRequest>()))
            .Callback<string, ConfigureGovernedServiceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<GovernedServiceDefinitionDto>.Success(FeeTypeDefinition()));

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() => Assert.Contains("only when the amount rule is", cut.Find(".gsw-fee-rule").TextContent), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Collect by fee type").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(GovernedServiceBasis.ApprovedFeeOption, sent!.Basis);
            Assert.True(sent.IsEnabled && sent.MobileEnabled);
            Assert.Empty(cut.FindAll(".gsw-fee-rule"));                                     // now in force: the notice goes away
        }, Timeout);
    }

    [Fact]
    public void FacilityActivation_TurnsTaboOnWithOneSwitch_SettingEnabledAndMobileTogether()
    {
        var tabo = Definition(CollectorOperationCodes.Tabo, "Tabo", GovernedServiceSetupState.SetupRequired, issues: "The amount rule has not been set up.") with
        { AllowedBases = [GovernedServiceBasis.DirectApprovedAmount] };
        Serve([tabo]);
        ConfigureGovernedServiceRequest? sent = null;
        _api.Setup(x => x.ConfigureAsync(CollectorOperationCodes.Tabo, It.IsAny<ConfigureGovernedServiceRequest>()))
            .Callback<string, ConfigureGovernedServiceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<GovernedServiceDefinitionDto>.Success(
                tabo with { State = GovernedServiceSetupState.Active, Basis = GovernedServiceBasis.DirectApprovedAmount, MobileEnabled = true, SetupIssues = [] }));

        var cut = RenderComponent<EEMOCantilanSDS.Client.Components.Shared.FacilityCanonicalCollection>(p => p
            .Add(x => x.OperationCode, CollectorOperationCodes.Tabo).Add(x => x.Name, "Tabo vendor fees"));

        cut.WaitForAssertion(() => Assert.Contains("Not turned on", cut.Markup), Timeout);
        Assert.DoesNotContain("Postman", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Turn on").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.True(sent!.IsEnabled && sent.MobileEnabled);                               // no half-enabled state can be saved
            Assert.Equal(GovernedServiceBasis.DirectApprovedAmount, sent.Basis);
            Assert.Null(sent.FixedAmount);                                                     // the existing fee schedule stays the amount authority
            Assert.Contains("On", cut.Find(".fcc-state").TextContent);
            Assert.Contains("Turn off", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void AddingAFeeType_SendsTheRuleTheHeadChose_AndShowsTheServersList()
    {
        Serve([FeeTypeDefinition()]);
        _api.Setup(x => x.GetFeeOptionsAsync(CollectorOperationCodes.MarketFees))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([]));
        _api.Setup(x => x.GetFeeOptionTotalsAsync(CollectorOperationCodes.MarketFees, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success([]));
        AddFeeOptionRequest? sent = null;
        _api.Setup(x => x.AddFeeOptionAsync(CollectorOperationCodes.MarketFees, It.IsAny<AddFeeOptionRequest>()))
            .Callback<string, AddFeeOptionRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([
                Option("Comfort Room", null, null, GovernedServiceBasis.FixedAmount, 5m, null, "Active")]));

        var cut = RenderComponent<MarketFees>();
        cut.WaitForAssertion(() => Assert.Contains("No fee type has been defined yet.", cut.Markup), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add fee type").Click();
        cut.Find("[role=dialog] form input[type=text]").Change("Comfort Room");
        cut.Find("[role=dialog] form input[type=number]").Change("5");
        cut.Find("[role=dialog] form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(("Comfort Room", GovernedServiceBasis.FixedAmount, (decimal?)5m, (decimal?)null),
                (sent!.DisplayName, sent.Basis, sent.FixedAmount, sent.MaximumAmount));
            Assert.Contains("Fee type added.", cut.Markup);
            Assert.DoesNotContain("No fee type has been defined yet.", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void TheFeeTypeSuccessNotice_ClearsItselfAfterAFewSeconds_ButAnErrorStays()
    {
        Serve([FeeTypeDefinition()]);
        _api.Setup(x => x.GetFeeOptionsAsync(CollectorOperationCodes.MarketFees))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([]));
        _api.Setup(x => x.GetFeeOptionTotalsAsync(CollectorOperationCodes.MarketFees, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success([]));
        _api.SetupSequence(x => x.AddFeeOptionAsync(CollectorOperationCodes.MarketFees, It.IsAny<AddFeeOptionRequest>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Success([
                Option("Comfort Room", null, null, GovernedServiceBasis.FixedAmount, 5m, null, "Active")]))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceFeeOptionDto>>.Failure("That fee type already exists."));

        var cut = RenderComponent<MarketFees>();
        cut.WaitForAssertion(() => Assert.Contains("No fee type has been defined yet.", cut.Markup), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add fee type").Click();
        cut.Find("[role=dialog] form input[type=text]").Change("Comfort Room");
        cut.Find("[role=dialog] form input[type=number]").Change("5");
        cut.Find("[role=dialog] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Fee type added.", cut.Markup), Timeout);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Fee type added.", cut.Markup), TimeSpan.FromSeconds(6));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add fee type").Click();
        cut.Find("[role=dialog] form input[type=text]").Change("Comfort Room");
        cut.Find("[role=dialog] form input[type=number]").Change("5");
        cut.Find("[role=dialog] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("That fee type already exists.", cut.Markup), Timeout);
        Thread.Sleep(3500);
        Assert.Contains("That fee type already exists.", cut.Markup);
    }
}
