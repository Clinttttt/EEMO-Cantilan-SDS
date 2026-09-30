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
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(_collections.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private void Serve(ObligationKind kind, params ObligationAccountDto[] accounts) =>
        _api.Setup(x => x.GetAccountsAsync(kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success(accounts));

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
            Assert.Contains("has been opened yet", cut.Markup);
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
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Open account"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Open account").Click();
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
            var row = Assert.Single(cut.FindAll("[aria-label='Lot rental accounts'] tbody tr"));
            Assert.Contains("Fiesta · Aug 15, 2026", row.TextContent);
            Assert.Contains("₱2,500.00", row.TextContent);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Change amount");
        }, Timeout);
    }

    [Fact]
    public void FailedLoad_SaysSo_InsteadOfSuggestingNoAccountExists()
    {
        _api.Setup(x => x.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental))
            .ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Failure("offline"));

        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("accounts couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.DoesNotContain("has been opened yet", cut.Markup);
        }, Timeout);
    }
}
