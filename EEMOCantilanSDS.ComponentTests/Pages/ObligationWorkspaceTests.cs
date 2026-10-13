using EEMOCantilanSDS.Application.Dtos.Settings;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Shared;
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
    private readonly Mock<IGovernedServicesApiClient> _governed = new();

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
        Services.AddSingleton(Mock.Of<ICollectionActivityApiClient>());
        Services.AddSingleton(_collections.Object);
        Services.AddSingleton(_governed.Object);
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

    [Fact]
    public void VegetableFruit_ModesKeepState_AndDailyExcludesMonthlyReceipts()
    {
        var space = Space() with { Kind = ObligationKind.VegetableFruitSpaceRental, SubjectLabel = "1" };
        Serve(ObligationKind.VegetableFruitSpaceRental, space);
        _governed.Setup(x => x.GetActivityAsync("VEGETABLE_FRUIT_SPACE_RENTAL", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success([
                new(Guid.NewGuid(), PhilippineTime.Today, DateTime.UtcNow, "SRC-DAILY", RevenueInstrumentType.CashTicket, GovernedServiceMode.DailyTransaction, "Daily holder", null, 50m, "Collector", "Posted", NetAmount: 40m),
                new(Guid.NewGuid(), PhilippineTime.Today, DateTime.UtcNow, "SRC-OLD-WHOLE", RevenueInstrumentType.OfficialReceipt, GovernedServiceMode.WholePayment, "Historical holder", null, 900m, "Collector", "Posted")]));
        var cut = RenderComponent<VegetableFruit>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find("#vf-monthly").TextContent), Timeout);
        Assert.True(cut.Find("#vf-daily").HasAttribute("hidden"));
        Assert.Empty(cut.FindAll("#vf-monthly a[href$='/import']"));
        Assert.Equal("Space Rentals", cut.Find("#vf-monthly .rental-summary-title").TextContent);
        Assert.Empty(cut.FindAll("#vf-monthly tbody a"));
        Assert.NotNull(cut.Find("#vf-monthly a[href='/operations/vegetable-fruit/space-holders']"));
        Assert.NotNull(cut.Find("#vf-monthly a[href='/operations/vegetable-fruit/manage-space-holders']"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact("vegetable-fruit-overview", cut.Markup);
        Assert.NotNull(cut.Find("#vf-monthly .toolbar-unified .vf-mode"));
        cut.Find("#vf-monthly .search-input").Input("Ana");
        cut.FindAll("#vf-monthly .filter-tab").Single(x => x.TextContent == "Partial").Click();
        cut.FindAll("#vf-monthly .vf-mode button").Single(x => x.TextContent == "Daily").Click();
        Assert.True(cut.Find("#vf-monthly").HasAttribute("hidden"));
        var daily = cut.Find("#vf-daily");
        Assert.DoesNotContain("Remaining", daily.TextContent);
        Assert.DoesNotContain("Historical holder", daily.TextContent);
        Assert.Contains("Daily holder", daily.TextContent);
        Assert.Contains("₱40.00", daily.TextContent);
        cut.Find("#vf-daily input[type='search']").Input("SRC-DAILY");
        var month = PhilippineTime.Today.AddMonths(-1).ToString("yyyy-MM");
        cut.Find("#vf-daily input[type='month']").Change(month);
        Assert.NotNull(cut.Find("#vf-daily .vf-daily-toolbar .vf-mode"));
        Assert.Single(cut.FindAll("#vf-daily .vf-mode [aria-pressed='true']"));
        SpaceHolderWorkspaceTests.SaveVisualArtifact("vegetable-fruit-daily", cut.Markup);
        var calls = _governed.Invocations.Count;
        cut.FindAll("#vf-daily .vf-mode button").Single(x => x.TextContent == "Monthly").Click();
        Assert.Equal("Ana", cut.Find("#vf-monthly .search-input").GetAttribute("value"));
        Assert.Equal("Partial", cut.Find("#vf-monthly .filter-tab.active").TextContent);
        Assert.Single(cut.FindAll("#vf-monthly .vf-mode [aria-pressed='true']"));
        cut.FindAll("#vf-monthly .vf-mode button").Single(x => x.TextContent == "Daily").Click();
        Assert.Equal("SRC-DAILY", cut.Find("#vf-daily input[type='search']").GetAttribute("value"));
        Assert.Equal(month, cut.Find("#vf-daily input[type='month']").GetAttribute("value"));
        Assert.Equal(calls, _governed.Invocations.Count);
    }

    [Fact]
    public void VegetableFruit_AddNewUsesOwnKindAndServerNumber_WithConditionalContract()
    {
        Serve(ObligationKind.VegetableFruitSpaceRental);
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).Callback<CreateObligationAccountRequest>(x => sent = x)
            .ReturnsAsync(Result<ObligationAccountDto>.Failure("stop"));
        var cut = RenderComponent<VegetableFruit>();
        cut.FindAll("#vf-monthly button").Single(x => x.TextContent.Trim() == "+ Add New").Click();
        Assert.Empty(cut.FindAll("#obw-subject"));
        Assert.Contains("Contract Effectivity Date", cut.Find("[role='dialog']").TextContent);
        cut.FindAll("[role='radio']").Single(x => x.TextContent.Contains("No contract")).Click();
        Assert.Empty(cut.FindAll("input[placeholder='As written on the contract']"));
        cut.Find("#obw-occupant").Input("Ana Reyes"); cut.Find("#obw-amount").Change("900");
        cut.Find("form[aria-label='Open account']").Submit();
        cut.WaitForAssertion(() => {
            Assert.NotNull(sent);
            Assert.Equal((ObligationKind.VegetableFruitSpaceRental, Guid.Empty, "", OccupancyArrangement.SpaceOnly), (sent!.Kind, sent.PayorId, sent.SubjectLabel, sent.Arrangement));
            Assert.Null(sent.ContractReference);
        }, Timeout);
    }

    [Fact]
    public void VegetableFruit_ImportOmitsNumberColumn_AndSendsBlankForServerAllocation()
    {
        ImportSpaceHoldersRequest? previewed = null;
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(x => previewed = x)
            .ReturnsAsync(Result<SpaceHolderImportPreview>.Failure("stop"));
        var cut = RenderComponent<VegetableFruitImport>();
        Assert.Contains(cut.FindAll(".imp-chip"), x => x.TextContent.Contains("Rent / Space No."));
        cut.Find(".imp-use-sample").Click();
        Assert.All(cut.FindAll("input[aria-label^='Rent / Space No.']"), cell => Assert.True(cell.HasAttribute("readonly")));
        cut.WaitForAssertion(() => {
            Assert.NotNull(previewed);
            Assert.All(previewed!.Rows, x => { Assert.Equal("", x.Account.SubjectLabel); Assert.Equal(ObligationKind.VegetableFruitSpaceRental, x.Account.Kind); });
        }, Timeout);
    }

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
            Assert.Contains("found", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record") || b.TextContent.Contains("Collect"));
        }, Timeout);
    }

    [Fact]
    public void ShowsApprovedAmountAssessedCollectedAndRemaining_AsTheServersFigures()
    {
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<KanmanggayManageSpaceHolders>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll(".registry-table tbody tr"));
            Assert.Contains("Ana Reyes", row.TextContent);
            Assert.Contains("Space K-4", row.TextContent);
            Assert.Contains("₱1,200.00", row.TextContent);
            Assert.Contains("Partial", row.TextContent);
            Assert.Contains("₱400.00", cut.FindComponent<RentalRegistryHero>().Markup);
            Assert.Contains("₱800.00", cut.FindComponent<RentalRegistryHero>().Markup);
            Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href")?.StartsWith("/collections/current") == true);
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

        var cut = RenderComponent<KanmanggayManageSpaceHolders>();
        cut.WaitForAssertion(() => cut.Find(".registry-table tbody tr"), Timeout);
        cut.Find(".registry-table tbody tr").Click();

        cut.WaitForAssertion(() =>
        {
            var period = Assert.Single(cut.FindAll("table[aria-label='Space K-4 periods'] tbody tr"));
            Assert.Contains("September 2026", period.TextContent);
            Assert.Contains("₱800.00", period.TextContent);
        }, Timeout);
        _api.Verify(x => x.GetRegisterAsync(account.Id), Times.Once);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("kanmanggay-management-periods", cut.Markup);
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
        cut.Find("#obw-occupant").Input("ana");
        Assert.Empty(cut.FindAll("#obw-subject"));
        cut.Find("input[placeholder='As written on the contract']").Change("LC-2026-014");
        cut.Find("#obw-amount").Change("1200");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((ObligationKind.KanmanggaySpaceRental, Guid.Empty, "", 1200m),
                (sent!.Kind, sent.PayorId, sent.SubjectLabel, sent.Amount));
            Assert.Null(sent.StallId);
            Assert.Equal((OccupancyArrangement.SignedContract, "LC-2026-014"), (sent.Arrangement, sent.ContractReference));
            Assert.Contains("account opened", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void AnAdmin_CanReadTheRegister_ButCannotOpenChangeOrEndAnAccount()
    {
        var context = this.AddTestAuthorization();
        context.SetAuthorized("admin").SetRoles("Admin");
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<KanmanggayManageSpaceHolders>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Space K-4", cut.Markup);
            var labels = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
            Assert.DoesNotContain("Open account", labels);
            Assert.DoesNotContain("Change amount", labels);
            Assert.DoesNotContain("End", labels);
            Assert.Equal(5, cut.FindAll(".registry-actions button").Count);
            Assert.All(cut.FindAll(".registry-actions button[disabled]"), b => Assert.True(b.HasAttribute("disabled")));
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
            var row = Assert.Single(cut.FindAll("[aria-label='Registered rentals'] tbody tr"));
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
        Assert.Single(cut.FindAll("[aria-label='Registered rentals'] tbody tr"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.Contains("Add Rent / Space Holder", drawer.TextContent);
            Assert.NotNull(drawer.QuerySelector("form[aria-label='Open account']"));
            Assert.Contains("Add Rent / Space Holder", drawer.QuerySelector("footer")!.TextContent);
            Assert.DoesNotContain("obw-layout", drawer.ParentElement!.ClassName ?? "");      // not squeezed into the account list's own grid
        }, Timeout);
        Assert.DoesNotContain(cut.FindAll("footer button"), b => b.TextContent.Trim() == "Cancel");     // one dismiss control: the header X
        cut.Find(".eemo-drawer-header-close").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[role='dialog']"));
            Assert.Single(cut.FindAll("[aria-label='Registered rentals'] tbody tr"));
        }, Timeout);
    }

    [Fact]
    public void AddNew_IsGroupedIntoSections_WithPersistedContractEvidenceAndSpaceOnlyFallback()
    {
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            var titles = drawer.QuerySelectorAll(".avm-section-label").Select(x => x.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "Lessee Information", "Rent / Space & Contract Details", "Rent / Space Information", "Rental Rates", "Additional Information", "Applicable Charges" }, titles);
            var basis = drawer.QuerySelectorAll("[role='radio']").Select(x => x.QuerySelector(".avm-choice-title")!.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "Signed lease contract", "No contract (space only)" }, basis);
            Assert.Contains("Contract Effectivity Date", drawer.TextContent);
            Assert.Contains("Contract Duration (No. of Years)", drawer.TextContent);
            Assert.NotNull(drawer.QuerySelector("#obw-contract-date"));
            Assert.NotNull(drawer.QuerySelector("input[placeholder='As written on the signed contract']"));
            Assert.NotNull(drawer.QuerySelector("input[placeholder='As written on the contract']"));
            Assert.Contains(drawer.QuerySelectorAll(".avm-input-locked"), x => x.TextContent.Trim() == "Kanmanggay");   // the operation is context, not a choice
            Assert.NotNull(drawer.QuerySelector(".eemo-drawer-header-icon svg"));                        // the stallholder drawer's own header
        }, Timeout);

        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("No contract")).Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.Contains("Occupying Since (month)", drawer.TextContent);
            Assert.DoesNotContain("Contract Effectivity", drawer.TextContent);
            Assert.Null(drawer.QuerySelector("input[placeholder='As written on the contract']"));         // no contract is implied for a space-only occupancy
        }, Timeout);
    }

    [Fact]
    public void AddNew_SendsTheOccupancyBasis_AndNoContractReferenceForSpaceOnly()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        _collections.Setup(x => x.SearchCollectionPayorsAsync("Ana Reyes")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()))
            .Callback<CreateObligationAccountRequest>(r => sent = r)
            .ReturnsAsync(Result<ObligationAccountDto>.Failure("stop"));

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        cut.WaitForAssertion(() => cut.Find("#obw-occupant"), Timeout);
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("No contract")).Click();
        cut.Find("#obw-occupant").Input("Ana Reyes");
        Assert.Empty(cut.FindAll("#obw-subject"));
        cut.Find("#obw-amount").Change("900");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((Guid.Empty, "", 900m, OccupancyArrangement.SpaceOnly, (string?)null),
                (sent!.PayorId, sent.SubjectLabel, sent.Amount, sent.Arrangement, sent.ContractReference));
        }, Timeout);
    }

    [Fact]
    public void AHolderJustAdded_AppearsAtOnce_EvenWithAStatusFilterOrSearchLeftOnTheList()
    {
        var existing = Space();
        var added = Space() with { Id = Guid.NewGuid(), SubjectLabel = "Space K-9", CollectedToDate = 0m, OutstandingToDate = 0m, AssessedToDate = 0m };
        _api.SetupSequence(x => x.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental))
            .ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([existing], 1200m, 400m, 800m)))
            .ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([existing, added], 1200m, 400m, 800m)));
        _collections.Setup(x => x.SearchCollectionPayorsAsync("Ana Reyes")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).ReturnsAsync(Result<ObligationAccountDto>.Success(added));

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[aria-label='Registered rentals'] tbody tr")), Timeout);
        cut.FindAll(".filter-tab").Single(t => t.TextContent.Trim() == "Paid").Click();            // hides the (partly paid) existing holder
        cut.WaitForAssertion(() => Assert.DoesNotContain("Space K-4", cut.FindComponent<EEMOCantilanSDS.Client.Components.Shared.RegisteredRentalTable<ObligationAccountDto>>().Markup), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        cut.Find("#obw-occupant").Input("Ana Reyes");
        cut.Find("#obw-amount").Change("900");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Registered rentals'] tbody tr").Select(r => r.TextContent).ToArray();
            Assert.Contains(rows, r => r.Contains("Space K-9"));
            Assert.Contains(rows, r => r.Contains("Space K-4"));
            Assert.Equal("All", cut.Find(".filter-tab.active").TextContent.Trim());
        }, Timeout);
    }

    [Fact]
    public void FutureMonthLotRegistrationWaitsForExplicitConfirmation()
    {
        Serve(ObligationKind.FiestaArawLotRental);
        var future = new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1).AddMonths(1);
        var cut = RenderComponent<FiestaAraw>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        cut.Find("#obw-occupant").Input("Advance holder");
        cut.Find("#obw-event-date").Change(future.ToString("yyyy-MM-dd"));
        cut.Find("#obw-amount").Change("2500");
        cut.Find("form[aria-label='Open account']").Submit();
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
        Assert.Contains("Register lot holder in advance?", cut.Find("[role='dialog']").TextContent);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("fiesta-araw-confirmation", cut.Markup);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void LotRegistrationInCurrentOrPastMonthDoesNotRequireConfirmation(int offset)
    {
        Serve(ObligationKind.FiestaArawLotRental);
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).ReturnsAsync(Result<ObligationAccountDto>.Failure("Server validation"));
        var cut = OpenLotForm();
        cut.Find("#obw-event-date").Change(PhilippineTime.Today.AddMonths(offset).ToString("yyyy-MM-dd"));
        cut.Find("form[aria-label='Open account']").Submit();
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
        Assert.Contains("Server validation", cut.Find("[role='alert']").TextContent);
        Assert.Empty(cut.FindAll("#obw-advance-form"));
    }

    [Theory]
    [InlineData("holder")]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("event")]
    public void ReviewingAdvanceRegistrationPreservesInputsAndConfirmsUpdatedValues(string field)
    {
        Serve(ObligationKind.FiestaArawLotRental);
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()))
            .Callback<CreateObligationAccountRequest>(r => sent = r).ReturnsAsync(Result<ObligationAccountDto>.Failure("Duplicate event account"));
        var cut = OpenLotForm();
        var future = PhilippineTime.Today.AddMonths(1);
        cut.Find("#obw-event-date").Change(future.ToString("yyyy-MM-dd"));
        cut.Find("form[aria-label='Open account']").Submit();
        cut.FindAll("button").Single(b => b.TextContent == "Review details").Click();
        Assert.Equal("Advance holder", cut.Find("#obw-occupant").GetAttribute("value"));
        Assert.Equal("2500", cut.Find("#obw-amount").GetAttribute("value"));
        if (field == "holder") cut.Find("#obw-occupant").Input("Updated holder");
        if (field == "amount") cut.Find("#obw-amount").Change("3000");
        if (field == "date") cut.Find("#obw-event-date").Change(future.AddDays(1).ToString("yyyy-MM-dd"));
        if (field == "event") { cut.Find(".fh-dd-trigger").Click(); cut.FindAll(".fh-dd-item").Single(b => b.TextContent.Contains("Araw")).Click(); }
        cut.Find("form[aria-label='Open account']").Submit();
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
        Assert.Contains(field == "holder" ? "Updated holder" : "Advance holder", cut.Find("#obw-advance-form").TextContent);
        cut.Find("#obw-advance-form").Submit();
        Assert.NotNull(sent);
        Assert.Equal(field == "holder" ? "Updated holder" : "Advance holder", sent!.ActualOccupant);
        Assert.Equal(field == "amount" ? 3000m : 2500m, sent.Amount);
        Assert.Equal(field == "event" ? LotRentalEvent.Araw : LotRentalEvent.Fiesta, sent.Event);
        Assert.Equal(field == "date" ? future.AddDays(1) : future, sent.EventDate);
        Assert.Equal(sent.EventDate, sent.ActiveFrom);
        Assert.Equal(Guid.Empty, sent.PayorId);
        Assert.Contains("Duplicate event account", cut.Find("[role='alert']").TextContent);
        Assert.Empty(_collections.Invocations);
        Assert.Empty(Mock.Get(Services.GetRequiredService<IBusinessPayorsApiClient>()).Invocations);
        cut.Find("form[aria-label='Open account']").Submit();
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
        Assert.NotNull(cut.Find("#obw-advance-form"));
    }

    [Fact]
    public async Task AdvanceConfirmationPreventsDuplicateSubmissionAndShowsSeparateSuccess()
    {
        Serve(ObligationKind.FiestaArawLotRental);
        var pending = new TaskCompletionSource<Result<ObligationAccountDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).Returns(pending.Task);
        var cut = OpenLotForm();
        cut.Find("#obw-event-date").Change(PhilippineTime.Today.AddMonths(1).ToString("yyyy-MM-dd"));
        cut.Find("form[aria-label='Open account']").Submit();
        var submit = cut.Find("#obw-advance-form");
        var first = submit.SubmitAsync();
        await submit.SubmitAsync();
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
        pending.SetResult(Result<ObligationAccountDto>.Success(Space() with { Kind = ObligationKind.FiestaArawLotRental }));
        await first;
        Assert.Empty(cut.FindAll("#obw-advance-form"));
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
        var notice = cut.Find(".obw-success[role='status'][aria-live='polite']");
        Assert.Equal("Lot account opened successfully.", notice.TextContent);
        Assert.Null(notice.Closest(".v3-panel"));
        Assert.Empty(_collections.Invocations);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("fiesta-araw-success", cut.Markup);
    }

    [Fact]
    public void InvalidLotDetailsAndCanceledFormNeverSubmit()
    {
        Serve(ObligationKind.FiestaArawLotRental);
        var cut = OpenLotForm();
        cut.Find("#obw-event-date").Change(PhilippineTime.Today.AddMonths(1).ToString("yyyy-MM-dd"));
        cut.Find("#obw-amount").Change("0");
        cut.Find("form[aria-label='Open account']").Submit();
        Assert.Contains("Enter the approved amount", cut.Find("[role='alert']").TextContent);
        cut.Find("#obw-amount").Change("2500");
        cut.Find("#obw-occupant").Input("");
        cut.Find("form[aria-label='Open account']").Submit();
        Assert.Contains("Enter the lot holder", cut.Find("[role='alert']").TextContent);
        cut.Find("#obw-occupant").Input("Advance holder");
        cut.Find("form[aria-label='Open account']").Submit();
        cut.FindAll("button").Single(b => b.TextContent == "Review details").Click();
        cut.Find(".eemo-drawer-header-close").Click();                                    // fields were changed: closing asks first
        Assert.Contains("unsaved changes", cut.Find("[role='alertdialog']").TextContent);
        cut.FindAll("button").Single(b => b.TextContent == "Discard changes").Click();
        Assert.Empty(cut.FindAll("[role='dialog']"));
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
    }

    private IRenderedComponent<FiestaAraw> OpenLotForm()
    {
        var cut = RenderComponent<FiestaAraw>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        cut.Find("#obw-occupant").Input("Advance holder");
        cut.Find("#obw-amount").Change("2500");
        return cut;
    }

    [Fact]
    public void FiestaAraw_AddNew_UsesTheSameDrawerFamily_WithAnExplicitEventChoice_AndAServerNumberWhenBlank()
    {
        Serve(ObligationKind.FiestaArawLotRental);
        _collections.Setup(x => x.SearchCollectionPayorsAsync("Ana Reyes")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()))
            .Callback<CreateObligationAccountRequest>(r => sent = r)
            .ReturnsAsync(Result<ObligationAccountDto>.Failure("stop"));

        var cut = RenderComponent<FiestaAraw>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.Equal(new[] { "Event & Occupant Information", "Rent / Space & Event Details" }, drawer.QuerySelectorAll(".avm-section-label").Select(x => x.TextContent.Trim()).ToArray());
            Assert.NotNull(drawer.QuerySelector(".fh-dd-trigger")); // Same custom select used for contract duration.
            Assert.NotNull(drawer.QuerySelector(".eemo-drawer-footer"));
        }, Timeout);

        cut.Find(".fh-dd-trigger").Click();
        cut.FindAll(".fh-dd-item").Single(b => b.TextContent.Contains("Araw")).Click();
        cut.Find("#obw-occupant").Input("Ana Reyes");
        cut.Find("#obw-amount").Change("2500");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((ObligationKind.FiestaArawLotRental, LotRentalEvent.Araw, string.Empty, 2500m),
                (sent!.Kind, sent.Event!.Value, sent.SubjectLabel, sent.Amount));                       // blank number: the server assigns it
            Assert.Null(sent.Arrangement);                                                              // basis belongs to Kanmanggay only
        }, Timeout);
    }

    [Fact]
    public void ImportList_IsOfferedOnlyInsideRegistration_WithoutSubmitting()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        var cut = RenderComponent<Kanmanggay>();

        Assert.DoesNotContain(cut.FindAll("button, a"), x => x.TextContent.Trim() == "Import list");
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "+ Add New").Click();
        var import = cut.Find(".add-new-footer-leading button");
        Assert.Equal("button", import.GetAttribute("type"));
        import.Click();
        Assert.EndsWith("/operations/kanmanggay/import", Services.GetRequiredService<NavigationManager>().Uri);
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
    }

    [Fact]
    public async Task RegistrationSaveBlocksImportCancelAndDuplicateSubmission()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        var pending = new TaskCompletionSource<Result<ObligationAccountDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).Returns(pending.Task);
        var cut = RenderComponent<Kanmanggay>();
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "+ Add New").Click();
        cut.Find("#obw-occupant").Input("Holder");
        cut.Find("#obw-amount").Change("1200");
        var form = cut.Find("form[aria-label='Open account']");
        var first = form.SubmitAsync();
        await form.SubmitAsync();
        Assert.All(cut.FindAll("footer button"), x => Assert.True(x.HasAttribute("disabled")));
        var before = Services.GetRequiredService<NavigationManager>().Uri;
        cut.Find(".add-new-footer-leading button").Click();
        Assert.Equal(before, Services.GetRequiredService<NavigationManager>().Uri);
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
        pending.SetResult(Result<ObligationAccountDto>.Success(Space()));
        await first;
        Assert.Empty(cut.FindAll("form[aria-label='Open account']"));
    }

    [Fact]
    public void Import_IsTheStallholderImportFamily_HeroThreeStepsUploadTemplateSampleAndManual_WithNoPasteBox()
    {
        var cut = RenderComponent<KanmanggayImport>();

        Assert.Equal("KANMANGGAY · BULK IMPORT", cut.Find(".imp-hero-eyebrow").TextContent.Trim().ToUpperInvariant());
        Assert.Equal("Import Kanmanggay Rent / Space Holders", cut.Find(".imp-hero-title").TextContent.Trim());
        Assert.Equal(new[] { "1 Upload", "2 Review & edit", "3 Save" }, cut.FindAll(".imp-step").Select(x => x.TextContent.Trim()).ToArray());
        Assert.Contains("active", cut.Find(".imp-step").ClassList);
        Assert.NotNull(cut.Find(".imp-drop input[type='file']"));
        Assert.Contains("Download CSV template", cut.Find(".imp-upload-foot").TextContent);
        Assert.NotNull(cut.Find(".imp-use-sample"));
        Assert.NotNull(cut.Find(".imp-enter-manually"));
        Assert.Empty(cut.FindAll("textarea"));                                        // no pasted-rows box: upload, sample or manual, as in ICE
        Assert.Equal(new[] { "Actual Occupant (Leasee)", "Name on Contract", "Rent / Space No.", "Effectivity Date", "No. of Years", "Area (sqm)", "Monthly Rental / Contract", "Actual Mo. Rental", "Whole Year Rental", "Area Loc.", "Delinquent (₱)" },
            cut.FindAll(".imp-chip").Select(x => x.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void Import_SampleData_FillsTheEditableGridAtOnce_WithoutPayors_AndDiscardGoesBack()
    {
        var cut = RenderComponent<KanmanggayImport>();

        cut.Find(".imp-use-sample").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("3", cut.Find(".imp-review-count").TextContent.Trim());
            Assert.Contains("active", cut.FindAll(".imp-step")[1].ClassList);
            Assert.Equal(new[] { "#", "Actual Occupant (Leasee)", "Name on Contract", "Rent / Space No.", "Effectivity Date", "No. of Years", "Area (sqm)", "Monthly Rental / Contract", "Actual Mo. Rental", "Whole Year Rental", "Area Loc.", "Delinquent (₱)" },
                cut.FindAll(".imp-table thead th").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).ToArray());
            Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count);
            Assert.All(cut.FindAll(".imp-table tbody tr"), row => Assert.Equal("Ready", row.QuerySelector(".shi-inline-status")?.GetAttribute("aria-label")));             // each sample row carries its own occupant
            Assert.NotNull(cut.Find(".imp-add-row"));
            Assert.Equal(new[] { "Cancel", "Import 3 ready rows" }, cut.FindAll(".imp-foot-bar .imp-btn").Select(x => x.TextContent.Trim()).ToArray());
            Assert.False(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled"));
        }, Timeout);

        cut.FindAll(".imp-review-actions button").Single(b => b.TextContent.Trim() == "Discard").Click();
        cut.WaitForAssertion(() => Assert.Contains("active", cut.FindAll(".imp-step")[0].ClassList), Timeout);
    }

    [Fact]
    public void Import_ACsvFileLoadsIntoTheSameGrid_ReadByItsOwnHeader()
    {
        var cut = RenderComponent<KanmanggayImport>();
        var csv = "Space No.,Actual Occupant,Occupancy basis,Contract reference,Contract Effectivity (month),Approved monthly rental,Closed on\r\n" +
                  "K-1,Ana Reyes,Signed lease contract,LC-1,2026-09,900,\r\nK-2,Ben Cruz,No contract (space only),,2026-09,700,\r\n";

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "list.csv"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2", cut.Find(".imp-review-count").TextContent.Trim());
            Assert.Contains("list.csv", cut.Find(".imp-review-sub").TextContent);
            Assert.Equal("LC-1", cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value"));
            Assert.True(cut.Find("input[aria-label='Contract reference of row 2']").HasAttribute("readonly"));      // no contract: no reference
        }, Timeout);
    }

    [Fact]
    public void Import_TheTypedOccupantIsTheSourceHolder_AndNoPayorIsEverChosenOrSearched()
    {
        ImportSpaceHoldersRequest? previewed = null;
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest r) =>
        {
            previewed = r;
            var rows = r.Rows.Select((row, i) => new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.Ready, null, null,
                Facts: new(row.Account, row.ClosedOn, SpaceNumberOrigin.Supplied, null))).ToList();
            return Task.FromResult(Result<SpaceHolderImportPreview>.Success(new(rows, true)));
        });
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count), Timeout);

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(previewed);
            Assert.All(previewed!.Rows, x => { Assert.Equal(Guid.Empty, x.Account.PayorId); Assert.False(string.IsNullOrWhiteSpace(x.Account.ActualOccupant)); });
            Assert.Equal("Maria Santos", previewed.Rows[0].Account.ActualOccupant);
            Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Link Payor"));
            Assert.Contains("3 ready", cut.Find(".shi-foot-note").TextContent);
        }, Timeout);
        _collections.Verify(x => x.SearchCollectionPayorsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Import_UsesTheServersPreview_ForStatusDuplicatesAndSuggestedNumbers_AndKeepsABlankNumberBlank()
    {
        _collections.Setup(x => x.SearchCollectionPayorsAsync(It.IsAny<string>())).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        ImportSpaceHoldersRequest? saved = null;
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest r) =>
        {
            var rows = r.Rows.Select((row, i) =>
            {
                var account = row.Account;
                if (string.IsNullOrWhiteSpace(account.ActualOccupant))
                    return new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.NeedsSourceHolder, "RequiresSourceHolder", "Enter the occupant.", Facts:
                        new(account with { SubjectLabel = "3" }, row.ClosedOn, SpaceNumberOrigin.ServerSuggested, null));
                return account.SubjectLabel == "2"
                    ? new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.Invalid, "DuplicateSpace", "This space already has an account.", Facts:
                        new(account, row.ClosedOn, SpaceNumberOrigin.Supplied, "Ana Reyes"))
                    : new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.Ready, null, null, Facts:
                        new(account with { SubjectLabel = string.IsNullOrWhiteSpace(account.SubjectLabel) ? "3" : account.SubjectLabel }, row.ClosedOn,
                            string.IsNullOrWhiteSpace(account.SubjectLabel) ? SpaceNumberOrigin.ServerSuggested : SpaceNumberOrigin.Supplied, "Ana Reyes"));
            }).ToList();
            return Task.FromResult(Result<SpaceHolderImportPreview>.Success(new(rows, rows.All(x => x.Status == SpaceHolderImportStatus.Ready))));
        });
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>()))
            .Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(2, 0, ["Row 2: duplicate space"])));

        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        // Sample rows: 1 Maria (signed), 2 Jose (space only), and a third with no number. Each row's occupant is its own holder.

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 ready", cut.Find(".shi-foot-note").TextContent);
            Assert.Contains("1 invalid", cut.Find(".shi-foot-note").TextContent);
            Assert.Contains("This space already has an account.", cut.FindAll(".imp-table tbody tr")[1].QuerySelector(".shi-inline-status")!.GetAttribute("title"));     // the server's reason, as given
            Assert.Contains("Auto · 3", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder"));
        }, Timeout);

        cut.Find(".imp-foot-bar .imp-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "1", string.Empty }, saved!.Rows.Select(x => x.Account.SubjectLabel).ToArray());   // a blank number stays blank: the server numbers it
            Assert.All(saved.Rows, x => { Assert.Equal(Guid.Empty, x.Account.PayorId); Assert.False(string.IsNullOrWhiteSpace(x.Account.ActualOccupant)); });
            Assert.Contains("active", cut.FindAll(".imp-step")[2].ClassList);
            Assert.Contains("2 rent / space holders imported", cut.Find(".imp-state-title").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Import_NeverAssumesTheBasis_AndASignedRowSendsItsContractReference()
    {
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-enter-manually").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".imp-table tbody tr")), Timeout);

        cut.Find("input[aria-label='Actual Occupant (Leasee) of row 1']").Input("Ana Reyes");
        Assert.True(cut.Find("input[aria-label='Contract reference of row 1']").HasAttribute("readonly"));              // no basis chosen yet: nothing to reference

        cut.Find("select[aria-label='Lease type of row 1']").Change("Signed lease contract");
        cut.WaitForAssertion(() => Assert.False(cut.Find("input[aria-label='Contract reference of row 1']").HasAttribute("readonly")), Timeout);
        cut.Find("input[aria-label='Contract reference of row 1']").Input("LC-2026-014");

        cut.Find("select[aria-label='Lease type of row 1']").Change("No contract (space only)");
        cut.WaitForAssertion(() =>
        {
            var reference = cut.Find("input[aria-label='Contract reference of row 1']");
            Assert.True(reference.HasAttribute("readonly"));                                                             // a space let without a contract carries none
            Assert.Equal(string.Empty, reference.GetAttribute("value") ?? string.Empty);
        }, Timeout);
    }

    // The office's own paper register (Name of Lessee per Signed Contract, Effectivity, No. of Years, Area, Monthly Rentals per Contract, Actual Monthly
    // Rentals, Whole Year Rental) reads into its own columns, and every one of them reaches the server instead of being dropped.
    private const string OfficeRegisterHeader =
        "Name of Lessee (Actual Occupants),Name of Lessee per Signed Contract,Stall / Space No.,Effectivity of Lessee Contract,No. of Years,Area,Monthly Rentals per Contract,Actual Monthly Rentals,Whole Year Rental\r\n";

    [Fact]
    public void Import_TheOfficeRegisterHeadersFillTheirOwnColumns_AndNameOnContractIsNeverTheContractReference()
    {
        var cut = RenderComponent<KanmanggayImport>();
        var csv = OfficeRegisterHeader + "Ana Reyes,Estate of Ana Reyes,K-1,2026-09-04,3,9.5,1000,900,10800\r\n";

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "register.csv"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Ana Reyes", cut.Find("input[aria-label='Actual Occupant (Leasee) of row 1']").GetAttribute("value"));
            Assert.Equal("Estate of Ana Reyes", cut.Find("input[aria-label='Name on Contract of row 1']").GetAttribute("value"));
            Assert.Equal(string.Empty, cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value") ?? string.Empty);   // not mixed up with the lessee
            Assert.Equal("2026-09-04", cut.Find("input[aria-label='Effectivity Date of row 1']").GetAttribute("value"));
            Assert.Equal("3", cut.Find("input[aria-label='No. of Years of row 1']").GetAttribute("value"));
            Assert.Equal("9.5", cut.Find("input[aria-label='Area (sqm) of row 1']").GetAttribute("value"));
            Assert.Equal("1000", cut.Find("input[aria-label='Monthly Rental / Contract of row 1']").GetAttribute("value"));
            Assert.Equal("900", cut.Find("input[aria-label='Actual Mo. Rental of row 1']").GetAttribute("value"));
            var wholeYear = cut.Find("input[aria-label='Whole Year Rental of row 1']");
            Assert.True(wholeYear.HasAttribute("readonly"));                                              // derived (12 × the monthly rental), never typed or read from the sheet
            Assert.Equal("10800.00", wholeYear.GetAttribute("value"));
            Assert.Equal("Ready", cut.Find(".imp-table tbody tr .shi-inline-status").GetAttribute("aria-label"));                        // a basis was read from the contract evidence, not guessed from nothing
        }, Timeout);
    }

    [Fact]
    public void Import_EveryContractFieldReachesTheServer_AndBillingStartsInTheEffectivityMonth()
    {
        ImportSpaceHoldersRequest? saved = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(1, 0, [])));
        var cut = RenderComponent<KanmanggayImport>();
        var csv = OfficeRegisterHeader + "Ana Reyes,Estate of Ana Reyes,K-1,2026-09-04,3,9.5,1000,900,10800\r\n";
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "register.csv"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".imp-table tbody tr")), Timeout);

        cut.Find(".imp-foot-bar .imp-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            var account = Assert.Single(saved!.Rows).Account;
            Assert.Equal("Ana Reyes", account.ActualOccupant);
            Assert.Equal("Estate of Ana Reyes", account.ContractName);
            Assert.Equal(new DateOnly(2026, 9, 4), account.ContractEffectiveDate);                        // the signed date itself
            Assert.Equal(new DateOnly(2026, 9, 1), account.ActiveFrom);                                   // billing from that month
            Assert.Equal((3, 9.5m, 1000m, 900m), (account.ContractYears, account.AreaSqm, account.ContractMonthlyRate, account.Amount));
            Assert.Equal(OccupancyArrangement.SignedContract, account.Arrangement);
            Assert.Null(account.ContractReference);
        }, Timeout);
    }

    [Theory]
    [InlineData("4", "A Kanmanggay signed contract runs three years")]
    [InlineData("0", "Years must be a whole number from 1 to 50")]
    public void Import_AKanmanggayTermOtherThanThreeYearsIsInvalid_NeverSilentlyChanged(string years, string reason)
    {
        var cut = RenderComponent<KanmanggayImport>();
        var csv = OfficeRegisterHeader + $"Ana Reyes,Estate of Ana Reyes,K-1,2026-09-04,{years},9.5,1000,900,\r\n";

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "register.csv"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(reason, cut.Find(".imp-table tbody tr .shi-inline-status").GetAttribute("title"));
            Assert.Contains("0 ready", cut.Find(".shi-foot-note").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Import_VegetableFruitKeepsWhateverTermTheContractStates_AndAnyColumnsLeftBlankStayBlank()
    {
        ImportSpaceHoldersRequest? saved = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(2, 0, [])));
        var cut = RenderComponent<VegetableFruitImport>();
        var csv = "Name of Lessee (Actual Occupants),Name of Lessee per Signed Contract,Effectivity of Lessee Contract,No. of Years,Area,Actual Monthly Rentals\r\n" +
                  "Lina Torres,Lina Torres,2026-08-10,1,6,500\r\nRoy Dela Cruz,,2026-08-01,,,400\r\n";
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "vf.csv"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".imp-table tbody tr").Count), Timeout);
        Assert.All(cut.FindAll("input[aria-label^='Rent / Space No.']"), cell => Assert.True(cell.HasAttribute("readonly")));                                       // Vegetable / Fruits numbers are the server's

        cut.Find("select[aria-label='Lease type of row 2']").Change("No contract (space only)");
        cut.Find(".imp-foot-bar .imp-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            var first = saved!.Rows[0].Account; var second = saved.Rows[1].Account;
            Assert.Equal((1, 6m), (first.ContractYears, first.AreaSqm));                                   // a one-year term is a legitimate Vegetable / Fruits contract
            Assert.Equal(OccupancyArrangement.SignedContract, first.Arrangement);
            Assert.Equal(OccupancyArrangement.SpaceOnly, second.Arrangement);
            Assert.Null(second.ContractYears); Assert.Null(second.ContractName); Assert.Null(second.ContractMonthlyRate); Assert.Null(second.AreaSqm);
        }, Timeout);
    }

    [Fact]
    public void Import_TheTwoAddRowButtonsChooseTheBasisExplicitly_AndEnterManuallyChoosesNone()
    {
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-enter-manually").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".imp-table tbody tr")), Timeout);
        Assert.Equal(string.Empty, cut.Find("select[aria-label='Lease type of row 1']").GetAttribute("value") ?? string.Empty);      // never assumed

        cut.Find(".imp-add-row").Click();
        cut.Find(".imp-add-space-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count);
            Assert.Equal("Signed lease contract", cut.Find("select[aria-label='Lease type of row 2']").GetAttribute("value"));
            Assert.Equal("No contract (space only)", cut.Find("select[aria-label='Lease type of row 3']").GetAttribute("value"));
            Assert.True(cut.Find("input[aria-label='Name on Contract of row 3']").HasAttribute("readonly"));         // a space let without a contract carries no contract data
            Assert.False(cut.Find("input[aria-label='Name on Contract of row 2']").HasAttribute("readonly"));
        }, Timeout);
        Assert.Contains("Add row with contract", cut.Find(".imp-add-row").TextContent);
        Assert.Contains("Add row for rent/space only", cut.Find(".imp-add-space-row").TextContent);
    }

    [Fact]
    public void FiestaArawImport_IsTheSameFamily_RowsNameTheirEvent_AndNeverMixThem()
    {
        var cut = RenderComponent<FiestaArawImport>();
        Assert.Equal("Import Fiesta / Araw Rent / Space Holders", cut.Find(".imp-hero-title").TextContent.Trim());
        Assert.Empty(cut.FindAll("textarea"));

        cut.Find(".imp-use-sample").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "#", "Actual Occupant (Leasee)", "Rent / Space No.", "Event", "Event date", "Approved Event Amount (₱)" },
                cut.FindAll(".imp-table thead th").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).ToArray());
            Assert.Equal("Fiesta", cut.Find("select[aria-label='Event of row 1']").GetAttribute("value"));
            Assert.Equal("Araw", cut.Find("select[aria-label='Event of row 3']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("select[aria-label^='Occupancy basis']"));                                          // basis is Kanmanggay's, not a lot rental's
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

    [Fact]
    public void AnOfficeWithNoConfirmedDurationPolicy_IsNotOfferedCantilansThreeYears_AndNeverSendsOne()
    {
        Serve(ObligationKind.KanmanggaySpaceRental, Space());
        var model = OperationsTenantConfigurationTests.SettingsApi(adopted: true, listed: ["KANMANGGAY_SPACE_RENTAL"]).Object.GetAsync().Result.Value!;
        var withNotice = model with
        {
            Items = model.Items.Select(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL" ? i with { PolicyNotice = "confirm" } : i).ToList(),
        };
        var settings = new Mock<IOperationSettingsApiClient>();
        settings.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Success(withNotice));
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.OperationNameService(settings.Object));
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>())).Callback<CreateObligationAccountRequest>(x => sent = x)
            .ReturnsAsync(Result<ObligationAccountDto>.Failure("not saved in this test", 400));

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.DoesNotContain("Contract Duration (No. of Years)", drawer.TextContent);   // no three-year choice is offered
            Assert.Contains("Policy confirmation required", drawer.TextContent);
            Assert.Contains("Contract Effectivity Date", drawer.TextContent);                // the rest of the contract is recorded as usual
        }, Timeout);
    }
}
