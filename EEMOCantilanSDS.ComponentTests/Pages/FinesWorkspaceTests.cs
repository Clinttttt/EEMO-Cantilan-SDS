using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Fines / Penalties (IA-049): the Head defines approved penalties, a fine reaches an Official Receipt only from one of
/// them in Current Collection, and this workspace reads the posted register. It never records money or offers a
/// free-text amount.
/// </summary>
public sealed class FinesWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IPenaltiesApiClient> _api = new();

    public FinesWorkspaceTests()
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

    private void Serve(IReadOnlyList<PenaltyDefinitionDto>? definitions = null, IReadOnlyList<PenaltyRegisterRowDto>? register = null)
    {
        _api.Setup(x=>x.GetAssessmentsAsync(null,null,It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([]));
        _api.Setup(x=>x.GetFinePoliciesAsync()).ReturnsAsync(Result<IReadOnlyList<FinePolicyDto>>.Success([]));
        _api.Setup(x=>x.GetFineSourcesAsync()).ReturnsAsync(Result<IReadOnlyList<FineSourceOption>>.Success([]));
        _api.Setup(x => x.GetDefinitionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success(definitions ?? []));
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success(register ?? []));
        _api.Setup(x => x.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<PenaltyWorkspaceSummaryDto>.Success(new(new(2026, 10, 1), new(2026, 10, 31), 0m, 0m, 0m, 0)));
    }

    private static PenaltyRegisterRowDto Row(string kind, string status = "Posted", decimal amount = 50m, decimal? effective = null,
        string? payer = null, string? origin = null, string? account = null, string? facility = null, string? reason = null) =>
        new(Guid.NewGuid(), new DateOnly(2026, 10, 5), DateTime.UtcNow, payer, origin, "LATE_PAYMENT", "Late payment penalty",
            $"SRC-{Guid.NewGuid():N}"[..14], amount, status, effective ?? (status == "Posted" ? amount : 0m), kind, account, facility, reason);

    [Fact]
    public void AmountsRenderAsFormattedPesos_NeverAsLiteralCode()
    {
        Serve(register: [Row(PenaltyRegisterKinds.Direct, amount: 50m)]);

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("ToString", cut.Markup);
            Assert.Equal("₱50.00", cut.Find("[aria-label='Fines collected'] tbody td.v3-num").TextContent.Trim());
            Assert.Contains("₱50.00", cut.Find("tfoot").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheHeroShowsServerFigures_AndAnUnavailableSummaryIsNotAZero()
    {
        Serve();
        _api.Setup(x => x.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(
            Result<PenaltyWorkspaceSummaryDto>.Success(new(new(2026, 10, 1), new(2026, 10, 31), 150m, 100m, 75m, 2)));

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            var hero = cut.Find("header.v3h").TextContent;
            Assert.Contains("₱150.00", hero);
            Assert.Contains("₱100.00", hero);
            Assert.Contains("₱75.00", hero);
            Assert.Contains("2 unpaid", hero);
            Assert.Equal("Fines & Penalties", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
        }, Timeout);

        _api.Setup(x => x.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<PenaltyWorkspaceSummaryDto>.Failure("offline"));
        var failed = RenderComponent<Fines>();
        failed.WaitForAssertion(() =>
        {
            var hero = failed.Find("header.v3h").TextContent;
            Assert.Contains("Unavailable right now", hero);
            Assert.DoesNotContain("₱0.00", hero);
        }, Timeout);
    }

    [Fact]
    public void CollectionsDistinguishDirectAssessedAndReceiptFines_WithoutImplyingAnAccountForDirectCash()
    {
        Serve(register: [
            Row(PenaltyRegisterKinds.Direct, reason: "Obstructing the walkway"),
            Row(PenaltyRegisterKinds.Direct, payer: "Walk-up payer"),
            Row(PenaltyRegisterKinds.Assessed, account: "Lisa Reyes · Space 4", facility: "Kanmanggay", reason: "Unauthorized extension"),
            Row(PenaltyRegisterKinds.Definition, payer: "Ben Cruz", origin: "Stall 4 · August rent")]);

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Fines collected'] tbody tr");
            Assert.Equal(4, rows.Count);
            Assert.Contains("Direct fine", rows[0].TextContent);
            Assert.Contains("Obstructing the walkway", rows[0].TextContent);
            Assert.Contains("Direct fine collection", rows[0].TextContent);
            Assert.DoesNotContain("Assessed account", rows[0].TextContent);
            Assert.Contains("Walk-up payer", rows[1].TextContent);
            Assert.Contains("Assessed fine payment", rows[2].TextContent);
            Assert.Contains("Lisa Reyes · Space 4", rows[2].TextContent);
            Assert.Contains("Kanmanggay", rows[2].TextContent);
            Assert.Contains("Fine on receipt", rows[3].TextContent);
            Assert.Contains("Stall 4 · August rent", rows[3].TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheTypeFilterNarrowsCollections_AndReversedFinesAreNotCounted()
    {
        Serve(register: [Row(PenaltyRegisterKinds.Direct), Row(PenaltyRegisterKinds.Assessed, account: "Lisa Reyes"),
            Row(PenaltyRegisterKinds.Direct, status: "Reversed")]);

        var cut = RenderComponent<Fines>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[aria-label='Fines collected'] tbody tr").Count), Timeout);
        cut.FindAll("select").Single(s => s.InnerHtml.Contains("Assessed fine payment")).Change(PenaltyRegisterKinds.Direct);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[aria-label='Fines collected'] tbody tr").Count);
            Assert.DoesNotContain("Lisa Reyes", cut.Find("[aria-label='Fines collected']").TextContent);
            Assert.Contains("₱50.00", cut.Find("tfoot").TextContent);
            Assert.DoesNotContain("₱100.00", cut.Find("tfoot").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheSectionTabsLiveInOneStickyContainer_AndTheOrMarkerIsALightPillOnTheHero()
    {
        Serve();
        var cut = RenderComponent<Fines>();

        var tabs = cut.Find(".fn-nav").QuerySelectorAll("[aria-label='Fines workspace views'] button");
        Assert.Equal(["Collections", "Assessments", "Penalty settings"], tabs.Select(t => t.TextContent.Trim()).ToArray());
        Assert.Contains("Official Receipt", cut.Find("header.v3h .fn-or").TextContent);

        // Pinned navigation and the legible marker are CSS contracts; guard them so a restyle cannot silently drop them.
        var css = File.ReadAllText("../../../../EEMOCantilanSDS.Client/Components/Pages/Menus/Fines.razor.css");
        Assert.Matches(@"\.fn-nav\s*\{[^}]*position:\s*sticky[^}]*top:\s*0", css);
        Assert.Matches(@"\.fn-or\s+::deep\s+\.ic\s*\{[^}]*background:\s*#ffffff", css);
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }

    [Fact]
    public void CollectionStatusesArePillsWithText_AndEveryRowHasANamedActivityAction()
    {
        var posted = Row(PenaltyRegisterKinds.Direct);
        var reversed = Row(PenaltyRegisterKinds.Direct, status: "Reversed", amount: 50m);
        Serve(register: [posted, reversed]);

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Fines collected'] tbody tr");
            Assert.Contains("pill-active", rows[0].QuerySelector(".status-pill")!.ClassName);
            Assert.Equal("Posted", rows[0].QuerySelector(".status-pill")!.TextContent.Trim());
            Assert.Contains("pill-danger", rows[1].QuerySelector(".status-pill")!.ClassName);
            Assert.Equal("Reversed", rows[1].QuerySelector(".status-pill")!.TextContent.Trim());
            // A reversed fine shows what it now contributes and what was originally posted.
            Assert.Contains("₱0.00", rows[1].QuerySelector("td.v3-num")!.TextContent);
            Assert.Contains("of ₱50.00 posted", rows[1].QuerySelector("td.v3-num")!.TextContent);
            var action = rows[0].QuerySelector("a")!;
            Assert.Contains(posted.ReferenceCode, action.GetAttribute("aria-label"));
            Assert.Equal("Activity", action.TextContent.Trim());
        }, Timeout);
    }

    [Fact]
    public void PenaltySettingsShowsAnActiveAndRetiredStatusPill()
    {
        Serve([Late(), new(Guid.NewGuid(), "ILLEGAL_VENDING", "Illegal vending", null, GovernedServiceBasis.DirectApprovedAmount, null, 200m, false, new DateOnly(2026, 3, 1))]);

        var cut = RenderComponent<Fines>();
        View(cut, "Penalty settings");

        cut.WaitForAssertion(() =>
        {
            var defs = cut.FindAll("[aria-label='Approved penalties'] tbody tr");
            Assert.Contains("pill-active", defs[0].QuerySelector(".status-pill")!.ClassName);
            Assert.Contains("pill-inactive", defs[1].QuerySelector(".status-pill")!.ClassName);
            Assert.Equal("Retired", defs[1].QuerySelector(".status-pill")!.TextContent.Trim());
        }, Timeout);
    }

    [Fact]
    public void TheHeadStartsEitherWorkflowFromCollections_WhileAnAdminCanStartNeither()
    {
        Serve();
        _api.Setup(x => x.GetDirectFineOptionsAsync()).ReturnsAsync(Result<DirectFineOptionsDto>.Success(new(new(2026, 10, 11), [])));

        var cut = RenderComponent<Fines>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assess fine").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindComponents<EEMOCantilanSDS.Client.Components.Shared.FineAssessmentsRegister>());
            Assert.Single(cut.FindComponents<EEMOCantilanSDS.Client.Components.Shared.FineAssessmentDrawer>());
            Assert.Equal("true", cut.FindAll("[aria-label='Fines workspace views'] button").Single(b => b.TextContent.Trim() == "Assessments").GetAttribute("aria-pressed"));
        }, Timeout);

        var again = RenderComponent<Fines>();
        again.FindAll("button").Single(b => b.TextContent.Trim() == "Record direct fine").Click();
        Assert.Single(again.FindComponents<EEMOCantilanSDS.Client.Components.Shared.DirectFineCollectionDrawer>());
        Assert.Empty(again.FindComponents<EEMOCantilanSDS.Client.Components.Shared.FineAssessmentDrawer>());
    }

    private static PenaltyDefinitionDto Late(decimal amount = 50m) => new(
        Guid.NewGuid(), "LATE_PAYMENT", "Late payment penalty", "Stall rent arrears", GovernedServiceBasis.FixedAmount,
        amount, null, true, new DateOnly(2026, 1, 1));

    [Fact]
    public void Route_IsOfficeOnly_AndTheWorkspaceRecordsNothing()
    {
        Assert.Equal("/operations/fines", Assert.Single(typeof(Fines).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(typeof(Fines).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Serve();

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Fines & Penalties", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            // Collections is the default view; nothing is collected until the Head opens the explicit drawer.
            Assert.Contains("No collections match this view", cut.Markup);
            Assert.Empty(cut.FindComponents<EEMOCantilanSDS.Client.Components.Shared.DirectFineCollectionDrawer>());
            Assert.DoesNotContain("aren't recorded in StallTrack yet", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record payment"));
            // No free-text charge anywhere until the Head opens the definition form or the collection drawer.
            Assert.Empty(cut.FindAll("input[type='number']"));
        }, Timeout);

        View(cut, "Assessments");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No saved assessments match", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record payment"));
            Assert.Empty(cut.FindAll("input[type='number']"));
        }, Timeout);
    }

    [Fact]
    public void ShowsApprovedPenaltiesAndThePostedRegisterWithTheOrTheyRodeOn()
    {
        Serve([Late(), new(Guid.NewGuid(), "ILLEGAL_VENDING", "Illegal vending", null, GovernedServiceBasis.DirectApprovedAmount, null, 200m, false, new DateOnly(2026, 3, 1))],
            [new(Guid.NewGuid(), new DateOnly(2026, 9, 30), DateTime.UtcNow, "Lisa Reyes", "Stall 4 · August rent",
                "LATE_PAYMENT", "Late payment penalty", "OR-000125", 50m, "Posted"),
             new(Guid.NewGuid(), new DateOnly(2026, 9, 29), DateTime.UtcNow, "Ben Cruz", null,
                "LATE_PAYMENT", "Late payment penalty", "OR-000120", 50m, "Reversed")]);

        var cut = RenderComponent<Fines>();
        View(cut,"Penalty settings");
        cut.WaitForAssertion(() =>
        {
            var defs = cut.FindAll("[aria-label='Approved penalties'] tbody tr");
            Assert.Equal(2, defs.Count);
            Assert.Contains("Fixed · ₱50.00", defs[0].TextContent);
            Assert.Contains("Manually approved · up to ₱200.00", defs[1].TextContent);
            Assert.Contains("Retired", defs[1].TextContent);
        }, Timeout);
        View(cut,"Collections");
        cut.WaitForAssertion(() => {
            var rows = cut.FindAll("[aria-label='Fines collected'] tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Lisa Reyes", rows[0].TextContent);
            Assert.Contains("Stall 4 · August rent", rows[0].TextContent);
            Assert.Contains("OR-000125", rows[0].TextContent);
            // A reversed fine is listed but not counted in the total.
            Assert.Contains("₱50.00", cut.Find("tfoot").TextContent);
            Assert.DoesNotContain("₱100.00", cut.Find("tfoot").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheHeadDefinesAPenalty_AsAnApprovedVersion_NotAFreeTextCharge()
    {
        Serve();
        DefinePenaltyRequest? sent = null;
        _api.Setup(x => x.DefineAsync(It.IsAny<DefinePenaltyRequest>()))
            .Callback<DefinePenaltyRequest>(r => sent = r)
            .ReturnsAsync(Result<PenaltyDefinitionDto>.Success(Late(75m)));

        var cut = RenderComponent<Fines>();
        View(cut,"Penalty settings");
        cut.WaitForAssertion(() => cut.Find("button.v3-btn-sm"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Define penalty").Click();
        cut.Find("form[aria-label='Define a penalty'] input").Change("LATE_PAYMENT");
        cut.FindAll("form[aria-label='Define a penalty'] input")[1].Change("Late payment penalty");
        cut.Find("form[aria-label='Define a penalty'] input[type='number']").Change("75");
        cut.Find("form[aria-label='Define a penalty']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal("LATE_PAYMENT", sent!.Code);
            Assert.Equal(GovernedServiceBasis.FixedAmount, sent.Basis);
            Assert.Equal(75m, sent.FixedAmount);
            Assert.Null(sent.MaximumAmount);
            Assert.Contains("Penalty saved", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void AnAdmin_CanReadPenaltiesAndTheRegister_ButCannotDefineOne()
    {
        var context = this.AddTestAuthorization();
        context.SetAuthorized("admin").SetRoles("Admin");
        Serve([Late()]);

        var cut = RenderComponent<Fines>();
        View(cut,"Penalty settings");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Late payment penalty", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Define penalty");
        }, Timeout);
    }

    [Fact]
    public void FailedLoads_SayTheyCouldNotBeLoaded_InsteadOfSuggestingNothingIsApproved()
    {
        _api.Setup(x => x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Failure("offline"));
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Failure("offline"));

        var cut = RenderComponent<Fines>();
        View(cut,"Penalty settings");

        cut.WaitForAssertion(() =>
        {
            var alerts = cut.FindAll("[role='alert']").Select(x => x.TextContent).ToList();
            Assert.Contains(alerts, a => a.Contains("Approved penalties couldn't be loaded"));
            Assert.DoesNotContain("No penalty has been approved yet", cut.Markup);
        }, Timeout);
    }
    private static void View(IRenderedComponent<Fines> cut,string label)=>cut.FindAll("[aria-label='Fines workspace views'] button").Single(x=>x.TextContent.Trim()==label).Click();
}
