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
/// Kanmanggay and Fiesta / Araw lot rental (IA-050) are Head-configured obligation accounts. The workspaces show what is
/// assessed, collected and owed, let only the Head open an account and approve its amount, and never record money.
/// </summary>
public sealed class ObligationWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid PayorId = Guid.NewGuid();
    private readonly Mock<IObligationsApiClient> _api = new();
    private readonly Mock<IEcfCollectionsApiClient> _collections = new();

    public ObligationWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(_collections.Object);
        Services.AddSingleton(Mock.Of<IBusinessPayorsApiClient>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private void Serve(ObligationKind kind, params ObligationAccountDto[] accounts)
    {
        _api.Setup(x => x.GetAccountsAsync(kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success(accounts));
        _api.Setup(x => x.GetWorkspaceAsync(kind)).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new(accounts,
            accounts.Sum(a => a.AssessedToDate), accounts.Sum(a => a.CollectedToDate), accounts.Sum(a => a.OutstandingToDate))));
    }

    private static ObligationAccountDto Space(decimal amount = 1200m, decimal assessed = 1200m, decimal collected = 400m) => new(
        Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental, "Kanmanggay Space Rental", PayorId, "Ana Reyes", null, null,
        "Space K-4", null, null, new DateOnly(2026, 9, 1), null, amount, new DateOnly(2026, 9, 1),
        assessed, collected, assessed - collected);

    [Theory]
    [InlineData(typeof(Kanmanggay), "/operations/kanmanggay", "Kanmanggay")]
    [InlineData(typeof(FiestaAraw), "/operations/fiesta-araw", "Fiesta / Araw")]
    public void Workspace_IsOfficeOnly_AndNoLongerSaysItIsNotRecorded(Type page, string route, string title)
    {
        Assert.Equal(route, Assert.Single(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(page.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Serve(ObligationKind.KanmanggaySpaceRental);
        Serve(ObligationKind.FiestaArawLotRental);

        var cut = Render(builder => { builder.OpenComponent(0, page); builder.CloseComponent(); });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(title, Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            Assert.DoesNotContain("aren't recorded in StallTrack yet", cut.Markup);
            Assert.Contains("found.", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record") || b.TextContent.Contains("Collect"));
        }, Timeout);
    }

    [Fact]
    public void ShowsApprovedAmountAssessedCollectedAndRemaining_AsTheServersFigures()
    {
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Kanmanggay accounts'] tbody tr"));
            Assert.Contains("Ana Reyes", row.TextContent);
            Assert.Contains("Space K-4", row.TextContent);
            Assert.Contains("₱1,200.00", row.TextContent);
            Assert.Contains("₱400.00", row.TextContent);
            Assert.Contains("₱800.00", row.TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/collections/current");
        }, Timeout);
    }

    [Fact]
    public void Periods_AreLoadedOnDemand_WithTheirOwnBalances()
    {
        var account = Space();
        Serve(ObligationKind.KanmanggaySpaceRental, account);
        _api.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success(new[]
        {
            new ObligationQuoteDto(account.Id, Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental, "Kanmanggay Space Rental",
                "Space K-4", new DateOnly(2026, 9, 1), 1200m, 400m, 800m, PayorId, "Ana Reyes", Guid.NewGuid(), true)
        }));

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Periods"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Periods").Click();

        cut.WaitForAssertion(() =>
        {
            var period = Assert.Single(cut.FindAll("table[aria-label='Space K-4 periods'] tbody tr"));
            Assert.Contains("September 2026", period.TextContent);
            Assert.Contains("₱800.00", period.TextContent);
        }, Timeout);
        _api.Verify(x => x.GetRegisterAsync(account.Id), Times.Once);
    }

    [Fact]
    public void TheHeadOpensAnAccount_WithAnExplicitPayorAndAnApprovedAmount()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        _collections.Setup(x => x.SearchCollectionPayorsAsync("ana")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()))
            .Callback<CreateObligationAccountRequest>(r => sent = r)
            .ReturnsAsync(Result<ObligationAccountDto>.Success(Space()));

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        var search = cut.Find("form[aria-label='Open account'] input[type='search']");
        search.Input("ana");
        search.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "a" });
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find("form[aria-label='Open account'] select").InnerHtml), Timeout);
        cut.Find("form[aria-label='Open account'] select").Change(PayorId.ToString());
        cut.FindAll("form[aria-label='Open account'] input[type='text']")[0].Change("Space K-4");
        cut.Find("form[aria-label='Open account'] input[type='number']").Change("1200");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((ObligationKind.KanmanggaySpaceRental, PayorId, "Space K-4", 1200m),
                (sent!.Kind, sent.PayorId, sent.SubjectLabel, sent.Amount));
            Assert.Null(sent.StallId);
            Assert.Contains("account opened", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void AnAdmin_CanReadTheRegister_ButCannotOpenChangeOrEndAnAccount()
    {
        var context = this.AddTestAuthorization();
        context.SetAuthorized("admin").SetRoles("Admin");
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Space K-4", cut.Markup);
            var labels = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
            Assert.DoesNotContain("Open account", labels);
            Assert.DoesNotContain("Change amount", labels);
            Assert.DoesNotContain("End", labels);
            Assert.Contains("Periods", labels);
        }, Timeout);
    }

    [Fact]
    public void ALotRentalShowsItsEventAndDate_NotAMonthlyAmount()
    {
        Serve(ObligationKind.FiestaArawLotRental, new ObligationAccountDto(Guid.NewGuid(), ObligationKind.FiestaArawLotRental,
            "Fiesta/Araw Lot Rental", PayorId, "Ana Reyes", null, null, "Lot F-12", LotRentalEvent.Fiesta,
            new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15), null, 2500m, new DateOnly(2026, 8, 15), 2500m, 0m, 2500m));

        var cut = RenderComponent<FiestaAraw>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Fiesta / Araw accounts'] tbody tr"));
            Assert.Contains("Fiesta · Aug 15, 2026", row.TextContent);
            Assert.Contains("₱2,500.00", row.TextContent);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Change amount");
        }, Timeout);
    }


    [Fact]
    public void AddNew_OpensADrawer_NotAnInlineForm_AndCancelLeavesThePageAsItWas()
    {
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        Assert.Empty(cut.FindAll("[role='dialog']"));
        Assert.Equal(1, cut.FindAll("[aria-label='Kanmanggay accounts'] tbody tr").Count);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.Contains("Open Kanmanggay account", drawer.TextContent);
            Assert.NotNull(drawer.QuerySelector("form[aria-label='Open account']"));
            Assert.Contains("Open account", drawer.QuerySelector("footer")!.TextContent);
            Assert.DoesNotContain("obw-layout", drawer.ParentElement!.ClassName ?? "");      // not squeezed into the account list's own grid
        }, Timeout);
        cut.FindAll("footer button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[role='dialog']"));
            Assert.Equal(1, cut.FindAll("[aria-label='Kanmanggay accounts'] tbody tr").Count);
        }, Timeout);
    }

    [Fact]
    public void ImportList_ReviewsEveryRow_NeverLinksAPayorByName_AndImportsOnlyTheReadyRows()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        _collections.Setup(x => x.SearchCollectionPayorsAsync("Ana Reyes")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        ImportSpaceHoldersRequest? sent = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>()))
            .Callback<ImportSpaceHoldersRequest>(r => sent = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(1, 0, [])));

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Import list"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Import list").Click();
        cut.WaitForAssertion(() => cut.Find("textarea[aria-label='Spreadsheet rows']"), Timeout);
        cut.Find("textarea[aria-label='Spreadsheet rows']").Change("K-1\tAna Reyes\t900\t2026-09\t\nK-2\tBen Cruz\tabc\t2026-09\t");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review rows").Click();

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("[aria-label='Import summary']").TextContent;
            Assert.Contains("0 Ready", summary);
            Assert.Contains("1 Needs Payor", summary);
            Assert.Contains("1 Invalid", summary);
            Assert.True(cut.FindAll("button").Single(b => b.TextContent.Contains("Import ready rows")).HasAttribute("disabled"));
        }, Timeout);

        cut.FindAll("[aria-label='Rows to import'] tbody tr")[0].QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Find Payor").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Use Ana Reyes · same name", cut.Markup);
            Assert.Contains("0 Ready", cut.Find("[aria-label='Import summary']").TextContent);
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Use Ana Reyes")).Click();
        cut.WaitForAssertion(() => Assert.Contains("1 Ready", cut.Find("[aria-label='Import summary']").TextContent), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Import ready rows (1)")).Click();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(sent!.Rows);                                        // only the ready row is sent
            Assert.Equal((PayorId, "K-1", 900m), (row.Account.PayorId, row.Account.SubjectLabel, row.Account.Amount));
            Assert.Empty(cut.FindAll("[role='dialog']"));                                 // back to the account list
            Assert.Contains("Imported 1 · Needs review 1", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void FailedLoad_SaysSo_InsteadOfSuggestingNoAccountExists()
    {
        _api.Setup(x => x.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental))
            .ReturnsAsync(Result<ObligationWorkspaceDto>.Failure("accounts couldn't be loaded"));

        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("accounts couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.DoesNotContain("spaces found.", cut.Markup);
        }, Timeout);
    }
}
