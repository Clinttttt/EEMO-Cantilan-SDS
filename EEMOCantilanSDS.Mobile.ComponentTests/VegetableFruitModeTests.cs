using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class VegetableFruitModeTests : TestContext
{
    private const string Code = CollectorOperationCodes.VegetableFruitSpaceRental;
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly MobileSessionService _session = new();
    private readonly List<PendingOperation> _queue = [];
    public VegetableFruitModeTests()
    {
        Services.AddSingleton(_session);
        Services.AddSingleton(_api.Object);
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => _queue.ToArray());
        store.Setup(x => x.AddIssuedDocumentOperationAsync(It.IsAny<PendingOperation>())).Callback<PendingOperation>(x => _queue.Add(x)).Returns(Task.CompletedTask);
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("test:collector");
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
        _api.Setup(x => x.GetOperationCapabilitiesAsync()).ReturnsAsync(Result<CollectorOperationCapabilitiesDto>.Success(
            new(_session.Menu.CollectorId, _session.Menu.Today, [new(Code, "Vegetable / Fruits", true, CollectorOperationCapabilityStatus.Ready, true, [])])));
        _api.Setup(x => x.GetOperationTermsAsync(Code, GovernedServiceMode.DailyTransaction)).ReturnsAsync(Result<GovernedServiceTermsDto>.Success(
            new(Code, "Vegetable / Fruits", true, GovernedServiceBasis.DirectApprovedAmount, null, null, RevenueInstrumentType.CashTicket, false)));
        _api.Setup(x => x.GetOperationRecordsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<GovernedServiceRecordDto>>.Success([]));
    }

    [Fact]
    public void ExplicitModesDoNotDependOnTwoGovernedVariantsAndKeepBothDrafts()
    {
        var view = RenderComponent<OperationCollection>(p => p.Add(x => x.Code, Code));
        Assert.Equal(new[] { "Monthly", "Daily" }, view.FindAll(".op-space-modes button").Select(x => x.TextContent));
        Assert.Equal("Daily", view.Find(".op-space-modes [aria-pressed=true]").TextContent);
        SaveVisual("mobile-vegetable-daily", view.Markup);
        view.Find(".op-entry input[type=number]").Input("65");
        view.FindAll(".op-space-modes button")[0].Click();
        var monthly = view.Find(".op-monthly");
        Assert.False(monthly.HasAttribute("hidden"));
        Assert.Contains("Search registered space holder", monthly.TextContent);
        Assert.Empty(monthly.QuerySelectorAll(".op-entry"));
        var editor = view.FindComponent<CollectionSessionEditor>().Instance;
        Assert.Equal(Code, editor.OnlyOperation);
        Assert.Equal(_session.Menu.Today, editor.BusinessDate);
        SaveVisual("mobile-vegetable-monthly", view.Markup);
        view.Find(".op-monthly input[type=search]").Input("Monthly holder");
        view.FindAll(".op-space-modes button")[1].Click();
        Assert.Equal("65", view.Find(".op-entry input[type=number]").GetAttribute("value"));
        view.FindAll(".op-space-modes button")[0].Click();
        Assert.Same(editor, view.FindComponent<CollectionSessionEditor>().Instance);
        Assert.Equal("Monthly holder", view.Find(".op-monthly input[type=search]").GetAttribute("value"));
        _api.Verify(x => x.GetOperationTermsAsync(Code, GovernedServiceMode.WholePayment), Times.Never);
        _api.Verify(x => x.GetOperationTermsAsync(Code, GovernedServiceMode.DailyTransaction), Times.Once);
    }

    [Fact]
    public void MonthlyHasNoExplanatoryParagraphUnderTheModeSelector_SoTheFinderStartsRightAfterIt()
    {
        var view = RenderComponent<OperationCollection>(p => p.Add(x => x.Code, Code));
        view.FindAll(".op-space-modes button")[0].Click();

        var monthly = view.Find(".op-monthly");
        Assert.DoesNotContain("The account supplies the periods", view.Markup);
        Assert.DoesNotContain("collection uses OR", view.Markup);
        Assert.Empty(monthly.QuerySelectorAll("p.op-instruction"));
        Assert.Empty(monthly.QuerySelectorAll(":scope > p"));                                       // no leading text block of any kind
        Assert.True(view.Find(".op-space-modes").NextElementSibling!.ClassList.Contains("op-monthly"));   // the finder follows the selector directly
    }

    [Fact]
    public void DailyUnavailableDoesNotHideMonthlyAccountDiscovery()
    {
        _api.Setup(x => x.GetOperationCapabilitiesAsync()).ReturnsAsync(Result<CollectorOperationCapabilitiesDto>.Failure("offline"));
        var view = RenderComponent<OperationCollection>(p => p.Add(x => x.Code, Code));
        Assert.Contains("Eligibility could not be verified", view.Markup);
        view.FindAll(".op-space-modes button")[0].Click();
        Assert.False(view.Find(".op-monthly").HasAttribute("hidden"));
        Assert.Single(view.FindComponents<CollectionSessionEditor>());
    }

    [Fact]
    public void DailyCaptureKeepsDirectCtModeAndDurableRetryIdentityWithoutMonthlyAccount()
    {
        var view = RenderComponent<OperationCollection>(p => p.Add(x => x.Code, Code));
        Assert.Contains("CT", view.Find(".op-facts").TextContent);
        view.Find(".op-entry input[type=number]").Input("65");
        view.Find(".op-cta").Click();
        view.WaitForAssertion(() => Assert.Single(_queue));
        var pending = _queue[0];
        Assert.Equal(OfflineOperationKind.GovernedService, pending.Kind);
        Assert.Equal(Code, pending.OperationCode);
        Assert.Equal(GovernedServiceMode.DailyTransaction, pending.CollectionMode);
        Assert.Equal(_session.Menu.Today, pending.BusinessDate);
        Assert.Equal(65m, pending.ReceivedAmount);
        Assert.NotEqual(Guid.Empty, pending.ClientOperationId);
        Assert.Null(pending.CollectionSession);
        Assert.Null(pending.ReferenceCode);
        Assert.Null(pending.PayerName);
        Assert.Empty(view.FindComponents<CollectionSessionEditor>());
    }

    [Fact]
    public void MonthlySearchNeverOffersDirectFallbackOrAnotherSameNameSource()
    {
        var own = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, Guid.NewGuid());
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([
            new(own, "Same name", "Vegetable / Fruits · Space 7", Code),
            new(new(SourceIdentityKind.SpaceAccount, Guid.NewGuid()), "Same name", "Kanmanggay · Space 7", CollectorOperationCodes.KanmanggaySpaceRental),
            new(new(SourceIdentityKind.FishMeatVendorRegistration, Guid.NewGuid()), "Same name", "Fish", CollectorOperationCodes.FishMeatVendorFee)]));
        var view = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.OnlyOperation, Code).Add(x => x.BusinessDate, _session.Menu.Today));
        view.Find("input[type=search]").Input("Same name");
        view.FindAll("button").Single(x => x.TextContent == "Search").Click();
        Assert.Single(view.FindAll(".payer-option"));
        Assert.Contains("Vegetable / Fruits", view.Find(".payer-option").TextContent);
        view.Find("input[type=search]").Input("No match");
        _api.Setup(x => x.SearchCollectionSourcesAsync("No match")).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([]));
        view.FindAll("button").Single(x => x.TextContent == "Search").Click();
        Assert.DoesNotContain("Continue with payer name", view.Markup);
        Assert.Empty(view.FindAll("input[type=number]"));
    }

    [Theory]
    [InlineData(CollectorOperationCodes.KanmanggaySpaceRental)]
    [InlineData(CollectorOperationCodes.FiestaArawLotRental)]
    public void Registered_space_operations_restrict_search_to_their_own_accounts_and_never_offer_direct_payment(string code)
    {
        var space = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, Guid.NewGuid());
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(
            Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([
                new(space, "Correct holder", "Registered space", code),
                new(new(SourceIdentityKind.SpaceAccount, Guid.NewGuid()), "Another area", "Other space",
                    code == CollectorOperationCodes.KanmanggaySpaceRental ? CollectorOperationCodes.FiestaArawLotRental : CollectorOperationCodes.KanmanggaySpaceRental),
                new(new(SourceIdentityKind.FishMeatVendorRegistration, Guid.NewGuid()), "Vendor", "Fish",
                    CollectorOperationCodes.FishMeatVendorFee)
            ]));
        var view = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, _session.Menu.Today).Add(x => x.OnlyOperation, code));
        Assert.Contains("Search registered space holder", view.Markup);
        view.Find("input[type=search]").Input("Correct");
        view.FindAll("button").Single(x => x.TextContent == "Search").Click();
        Assert.Single(view.FindAll(".payer-option"));
        Assert.Contains("Correct holder", view.Find(".payer-option").TextContent);

        _api.Setup(x => x.SearchCollectionSourcesAsync("Missing")).ReturnsAsync(
            Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([]));
        view.Find("input[type=search]").Input("Missing");
        view.FindAll("button").Single(x => x.TextContent == "Search").Click();
        Assert.Empty(view.FindAll(".payer-option"));
        Assert.DoesNotContain("Continue with payer name", view.Markup);
    }

    [Theory]
    [InlineData(CollectorOperationCodes.KanmanggaySpaceRental)]
    [InlineData(CollectorOperationCodes.FiestaArawLotRental)]
    public void Deep_linked_new_collection_sets_the_selected_space_operation(string code)
    {
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/collections/new?operation=" + code);
        var view = RenderComponent<NewCollection>();
        view.WaitForAssertion(() => Assert.Equal(code, view.FindComponent<CollectionSessionEditor>().Instance.OnlyOperation));
        Assert.Contains("Search registered space holder", view.Markup);
    }

    [Fact]
    public void MonthlyExplainsServerUnavailableSetup()
    {
        var id = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, Guid.NewGuid());
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([new(id, "Holder", "Space 7", Code)]));
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(id)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, _session.Menu.Today, [
            new(CollectionSessionItemKind.Obligation, Code, "Monthly space rental", true, false, "NeedsPolicy", "Monthly OR policy is not configured.", false, [])], SourceIdentity: id)));
        var view = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.OnlyOperation, Code));
        view.Find("input[type=search]").Focus();
        view.Find(".payer-option").Click();
        Assert.Contains("Monthly OR policy is not configured", view.Markup);
        Assert.DoesNotContain("Continue as direct collection", view.Markup);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(null), Times.Never);
    }

    [Fact]
    public void MonthlySelectsTheActualAccountAndPeriodWithServerApprovedOrBalance()
    {
        var id = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, Guid.NewGuid());
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([new(id, "Holder", "Space 7", Code)]));
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(id)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, _session.Menu.Today, [
            new(CollectionSessionItemKind.Obligation, Code, "Monthly space rental", true, true, null, null, false, [], Choices: [
                new("period", CollectionSessionItemKind.Obligation, Code, "Monthly space rental", "Space 7 · September 2026", new(AccountId: id.Id, PeriodStart: new(2026, 9, 1)),
                    RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance, 275m, 275m)])], SourceIdentity: id)));
        CollectionSessionIntent? sent = null;
        _api.Setup(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>())).Callback<CollectionSessionIntent>(x => sent = x)
            .ReturnsAsync(Result<CollectionSessionQuote>.Failure("stop after quote"));
        var view = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.OnlyOperation, Code).Add(x => x.BusinessDate, _session.Menu.Today));
        view.Find("input[type=search]").Focus();
        view.Find(".payer-option").Click();
        view.FindAll("button").Single(x => x.TextContent == "+ Add item").Click();
        Assert.Contains("OR", view.Find("[role=dialog]").TextContent);
        Assert.Equal("275", view.Find("[role=dialog] input[type=number]").GetAttribute("value"));
        view.FindAll("button").Single(x => x.TextContent == "Add item").Click();
        view.FindAll("button").Single(x => x.TextContent == "Review collection").Click();
        Assert.NotNull(sent);
        Assert.Equal(id, sent!.SourceIdentity);
        Assert.Null(sent.PayorId);
        var item = Assert.Single(sent.Items);
        Assert.Equal(new SessionObligationIntent(id.Id, 2026, 9), item.Obligation);
        Assert.Equal(275m, item.ConfirmedAmount);
        Assert.Null(item.Service);
    }

    internal static void SaveVisual(string name, string markup)
    {
        var output = Environment.GetEnvironmentVariable("STALLTRACK_MOBILE_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EEMOCantilanSDS.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var mobile = Path.Combine(root!.FullName, "EEMOCantilanSDS.Mobile");
        string Url(string path) => new Uri(path).AbsoluteUri;
        var styles = new[] { "wwwroot/css/site.min.css", "wwwroot/app.css", "wwwroot/css/eemo.css", "Components/Pages/Menus/OperationCollection.razor.css", "Components/Shared/CollectionSessionEditor.razor.css", "Components/Shared/BottomNav.razor.css", "Components/Shared/MobileChoice.razor.css" };
        var links = string.Join("", styles.Select(path => $"<link rel='stylesheet' href='{Url(Path.Combine(mobile, path))}'>"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, name + ".html"), $"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><base href='{Url(Path.Combine(mobile, "wwwroot"))}/'>{links}<style>*,html,body{{margin:0;padding:0;box-sizing:border-box}}html,body{{height:100%}}.full-layout{{height:100dvh;display:flex;flex-direction:column}}</style></head><body><div id='app'><div class='full-layout'>{markup}</div></div></body></html>");
    }
}
