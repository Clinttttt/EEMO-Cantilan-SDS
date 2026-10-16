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

// Keep the import-preview and drawer tests separate from concurrently running test collections;
// the full suite intermittently lost a row editor in the default parallel configuration.
[CollectionDefinition("ObligationWorkspaceSerial", DisableParallelization = true)]
public sealed class ObligationWorkspaceSerialCollection { }

/// <summary>
/// Kanmanggay and Fiesta / Araw lot rental (IA-050) are Head-configured obligation accounts. The workspaces show what is
/// assessed, collected and owed, let only the Head open an account and approve its amount, and never record money.
/// </summary>
[Collection("ObligationWorkspaceSerial")]
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
        JSInterop.SetupModule("./js/import-status.js").Mode = JSRuntimeMode.Loose;
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
    public void VegetableFruitAddNewDefaultsThreeYearsAndAllowsOtherRecordedTerms()
    {
        Serve(ObligationKind.VegetableFruitSpaceRental, Space() with { Kind = ObligationKind.VegetableFruitSpaceRental });
        var cut = RenderComponent<VegetableFruit>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        var duration = cut.FindComponents<FhSelect>().Single(x => x.Instance.AriaLabel == "Contract Duration (No. of Years)");
        Assert.Equal("3", duration.Instance.Value);
        Assert.Null(duration.Instance.DisabledValues);
        duration.Find("button").Click();
        duration.FindAll("[role='option']").Single(x => x.TextContent.Trim() == "1 year").Click();
        Assert.Equal("1", duration.Instance.Value);
        duration.Find("button").Click();
        duration.FindAll("[role='option']").Single(x => x.TextContent.Trim() == "Custom…").Click();
        cut.Find("input[aria-label='Custom contract duration in years']").Input("7");
        Assert.Equal("7", cut.Find("input[aria-label='Custom contract duration in years']").GetAttribute("value"));
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
            Assert.Equal(new[] { "Lessee Information", "Rent / Space & Event Details", "Space Information", "Rental Rates", "Additional Information", "Applicable Charges" }, drawer.QuerySelectorAll(".avm-section-label").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Equal(new[] { "Signed lease contract", "No contract (space only)" }, drawer.QuerySelectorAll(".avm-choice-title").Select(x => x.TextContent.Trim()));
            Assert.Contains("Not recorded for event lots", drawer.TextContent);
            Assert.DoesNotContain("Whole Year:", drawer.TextContent);
            Assert.DoesNotContain("Whole-year equivalent", drawer.TextContent);
            Assert.Null(drawer.QuerySelector("input[placeholder='As written on the signed contract']"));
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
            Assert.Equal(OccupancyArrangement.SpaceOnly, sent.Arrangement);
            Assert.Null(sent.ContractReference);
            Assert.Null(sent.ContractName); Assert.Null(sent.ContractYears); Assert.Null(sent.ContractEffectiveDate);
            Assert.Null(sent.ContractMonthlyRate); Assert.Null(sent.AreaSqm); Assert.Null(sent.AreaLocation);
        }, Timeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EventBasisAndReferenceAreSavedWithoutMonthlyContractMetadata(bool signed)
    {
        Serve(ObligationKind.FiestaArawLotRental);
        CreateObligationAccountRequest? sent = null;
        _api.Setup(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()))
            .Callback<CreateObligationAccountRequest>(r => sent = r)
            .ReturnsAsync(Result<ObligationAccountDto>.Failure("stop"));
        var cut = OpenLotForm();
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("Signed lease contract")).Click();
        cut.Find("#obw-event-reference").Change("EVENT-LEASE-14");
        if (!signed) cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("No contract")).Click();
        cut.Find("form[aria-label='Open account']").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(signed ? OccupancyArrangement.SignedContract : OccupancyArrangement.SpaceOnly, sent!.Arrangement);
            Assert.Equal(signed ? "EVENT-LEASE-14" : null, sent.ContractReference);
            Assert.Equal(sent.EventDate, sent.ActiveFrom);
            Assert.Equal(2500m, sent.Amount);
            Assert.Null(sent.ContractYears); Assert.Null(sent.ContractName); Assert.Null(sent.ContractEffectiveDate);
            Assert.Null(sent.ContractMonthlyRate); Assert.Null(sent.AreaSqm); Assert.Null(sent.AreaLocation);
        }, Timeout);
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Once);
    }

    [Theory]
    [InlineData("basis")]
    [InlineData("event")]
    [InlineData("amount")]
    public void EventCustomControlsProtectUnsavedChanges(string control)
    {
        Serve(ObligationKind.FiestaArawLotRental);
        var cut = RenderComponent<FiestaAraw>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        if (control == "basis") cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("Signed lease contract")).Click();
        else if (control == "event")
        {
            cut.Find(".fh-dd-trigger").Click();
            cut.FindAll(".fh-dd-item").Single(b => b.TextContent.Contains("Araw")).Click();
        }
        else cut.Find("button[aria-label='Increase event amount']").Click();
        cut.Find(".eemo-drawer-header-close").Click();
        Assert.Contains("unsaved changes", cut.Find("[role='alertdialog']").TextContent);
        cut.FindAll("button").Single(b => b.TextContent == "Keep editing").Click();
        Assert.Single(cut.FindAll("[role='dialog']"));
        cut.Find(".eemo-drawer-header-close").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Discard changes").Click();
        Assert.Empty(cut.FindAll("[role='dialog']"));
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    [InlineData(ObligationKind.FiestaArawLotRental)]
    public void SpaceImportsKeepDetailsAndRemovalWithoutStatusMarkers(ObligationKind kind)
    {
        var cut = RenderComponent<SpaceHolderImport>(p => p.Add(x => x.Kind, kind));
        cut.Find(".imp-enter-manually").Click();
        Assert.Empty(cut.FindAll(".shi-inline-status"));
        var details = cut.Find(".shi-row-edit");
        Assert.DoesNotContain("@if", details.TextContent);
        Assert.Equal("Review", details.TextContent.Trim());
        Assert.Equal("Invalid", cut.Find(".shi-chip-status").TextContent);
        details.Click();
        Assert.False(string.IsNullOrWhiteSpace(cut.Find(".shi-editor-error[role='status']").TextContent));
        Assert.DoesNotContain("@if", cut.Find("[role='dialog']").TextContent);
        Assert.Contains("0 ready", cut.Find(".shi-foot-note").TextContent);
        Assert.Contains("1 invalid", cut.Find(".shi-foot-note").TextContent);
        Assert.True(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled"));
        cut.Find(".eemo-drawer-header-close").Click();
        cut.Find("button[aria-label='Remove row 1']").Click();
        Assert.Empty(cut.FindAll(".imp-table tbody tr"));
        Assert.NotNull(cut.Find(".imp-enter-manually"));
    }

    [Theory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    [InlineData(ObligationKind.FiestaArawLotRental)]
    public void ImportNewManualDateUsesPhilippineTodayOnlyForMonthlyRows(ObligationKind kind)
    {
        var cut = RenderComponent<SpaceHolderImport>(p => p.Add(x => x.Kind, kind));
        cut.Find(".imp-enter-manually").Click();
        var date = cut.Find("input[type='date']");
        Assert.Equal(kind == ObligationKind.FiestaArawLotRental ? "" : PhilippineTime.Today.ToString("yyyy-MM-dd"), date.GetAttribute("value"));
    }

    [Theory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    public void MonthlySpaceImportHasNoLeaseChoice_DefaultsThreeYears_AndRecordsExplicitContractEvidence(ObligationKind kind)
    {
        ImportSpaceHoldersRequest? saved = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(3, 0, [])));
        IRenderedFragment cut = kind == ObligationKind.KanmanggaySpaceRental
            ? RenderComponent<KanmanggayImport>() : RenderComponent<VegetableFruitImport>();
        cut.Find(".imp-enter-manually").Click();
        Assert.Equal("3", cut.Find("input[aria-label='No. of Years of row 1']").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".imp-add-space-row"));
        cut.Find("button[aria-label='Edit row details 1']").Click();
        Assert.Empty(cut.FindAll("select[aria-label^='Lease type']"));
        Assert.Equal("", cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value"));
        cut.Find(".eemo-drawer-header-close").Click();
        cut.FindAll(".imp-review-actions button").Single(x => x.TextContent == "Discard").Click();
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count), Timeout);
        Assert.Equal("", cut.Find(".imp-table input[aria-label='No. of Years of row 2']").GetAttribute("value"));
        cut.Find(".imp-table input[aria-label='Name on Contract of row 2']").Input("Recorded Jose contract");
        cut.Find(".imp-table input[aria-label='No. of Years of row 2']").Input("1");
        cut.Find("button[aria-label='Edit row details 2']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input[aria-label='Contract reference of row 2']")), Timeout);
        cut.Find("input[aria-label='Contract reference of row 2']").Input("SIGNED-JOSE-1");
        cut.Find("button[aria-label='Contract duration options of row 2']").Click();
        cut.WaitForAssertion(() => Assert.Equal(6, cut.FindAll(".fh-dd-menu [role='option']").Count), Timeout);
        Assert.All(cut.FindAll(".fh-dd-menu [role='option']"), option => Assert.NotEqual("true", option.GetAttribute("aria-disabled")));
        cut.FindAll(".fh-dd-menu [role='option']").Single(x => x.TextContent.Contains("Custom")).Click();
        cut.Find(".shi-editor input[aria-label='No. of Years of row 2']").Input("7");
        Assert.Equal("7", cut.Find(".imp-table input[aria-label='No. of Years of row 2']").GetAttribute("value"));
        cut.Find(".eemo-drawer-header-close").Click();
        SubmitReadyImport(cut);
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(saved);
            Assert.Equal(7, saved.Rows[1].Account.ContractYears);
            Assert.Equal(OccupancyArrangement.SignedContract, saved.Rows[1].Account.Arrangement);
            Assert.Equal(1200m, saved.Rows[1].Account.Amount);
            Assert.Equal(OccupancyArrangement.SpaceOnly, saved.Rows[2].Account.Arrangement);
            Assert.Null(saved.Rows[2].Account.ContractYears);
        }, Timeout);
    }

    private static void SubmitReadyImport(IRenderedFragment cut)
    {
        cut.WaitForAssertion(() => Assert.False(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled")), Timeout);
        cut.Find(".imp-foot-bar .imp-btn-primary").Click();
    }

    private static SpaceHolderImportPreview NumberPreview(ImportSpaceHoldersRequest request, string automaticNumber) => new(
        request.Rows.Select((row, i) => new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.Ready, null, null,
            Facts: new(row.Account with { SubjectLabel = string.IsNullOrWhiteSpace(row.Account.SubjectLabel) ? automaticNumber : row.Account.SubjectLabel },
                row.ClosedOn, string.IsNullOrWhiteSpace(row.Account.SubjectLabel) ? SpaceNumberOrigin.ServerSuggested : SpaceNumberOrigin.Supplied, null))).ToArray(), true);

    [Fact]
    public async Task ImportOlderPreviewCannotOverwriteNewerRowEditsOrNumberSuggestions()
    {
        var oldReply = new TaskCompletionSource<Result<SpaceHolderImportPreview>>();
        var newReply = new TaskCompletionSource<Result<SpaceHolderImportPreview>>();
        var firstStarted = new TaskCompletionSource<ImportSpaceHoldersRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<ImportSpaceHoldersRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new System.Collections.Concurrent.ConcurrentQueue<ImportSpaceHoldersRequest>();
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
        {
            requests.Enqueue(request);
            if (requests.Count == 1) firstStarted.TrySetResult(request); else secondStarted.TrySetResult(request);
            return requests.Count == 1 ? oldReply.Task : newReply.Task;
        });
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        await firstStarted.Task.WaitAsync(Timeout);
        cut.Find("input[aria-label='Actual Occupant (Leasee) of row 3']").Input("Ana Edited");
        await secondStarted.Task.WaitAsync(Timeout);
        var snapshots = requests.ToArray();
        await cut.InvokeAsync(() => newReply.SetResult(Result<SpaceHolderImportPreview>.Success(NumberPreview(snapshots[1], "9"))));
        cut.WaitForAssertion(() => Assert.Equal("Auto · 9", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder")), Timeout);
        await cut.InvokeAsync(async () => { oldReply.SetResult(Result<SpaceHolderImportPreview>.Success(NumberPreview(snapshots[0], "99"))); await Task.Yield(); });
        cut.FindComponent<SpaceHolderImport>().Render();
        Assert.Equal("Auto · 9", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder"));
        Assert.Equal("Ana Edited", cut.Find("input[aria-label='Actual Occupant (Leasee) of row 3']").GetAttribute("value"));
    }

    [Fact]
    public void ImportAddingRowRefreshesTheServerPreview()
    {
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
            Task.FromResult(Result<SpaceHolderImportPreview>.Success(NumberPreview(request, "4"))));
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() => _api.Verify(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>()), Times.Once), Timeout);
        cut.Find(".imp-add-row").Click();
        cut.WaitForAssertion(() => _api.Verify(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>()), Times.Exactly(2)), Timeout);
    }

    [Fact]
    public void ImportTableAndDrawerShareContractRateAndDateEdits()
    {
        var cut = RenderComponent<VegetableFruitImport>();
        cut.Find(".imp-use-sample").Click();
        cut.Find(".imp-table input[aria-label='Name on Contract of row 1']").Input("Estate of Maria");
        cut.Find(".imp-table input[aria-label='Effectivity Date of row 1']").Input("2026-03-17");
        cut.Find("button[aria-label='Edit row details 1']").Click();
        Assert.Equal("Estate of Maria", cut.Find(".shi-editor input[aria-label='Name on Contract of row 1']").GetAttribute("value"));
        cut.Find(".shi-editor input[aria-label='Monthly Rental / Contract of row 1']").Input("1000");
        cut.Find(".shi-editor input[aria-label='Actual Mo. Rental of row 1']").Input("900");
        Assert.Equal("1000", cut.Find(".imp-table input[aria-label='Monthly Rental / Contract of row 1']").GetAttribute("value"));
        Assert.Equal("10800.00", cut.Find(".imp-table input[aria-label='Whole Year Rental of row 1']").GetAttribute("value"));
        cut.Find(".imp-table input[aria-label='No. of Years of row 1']").Input("2");
        Assert.Equal("2026-03-17", cut.Find(".imp-table input[aria-label='Effectivity Date of row 1']").GetAttribute("value"));
        Assert.False(cut.Find(".imp-table input[aria-label='Name on Contract of row 1']").HasAttribute("readonly"));
        Assert.Equal("Estate of Maria", cut.Find(".imp-table input[aria-label='Name on Contract of row 1']").GetAttribute("value"));
        Assert.Equal("2", cut.Find(".imp-table input[aria-label='No. of Years of row 1']").GetAttribute("value"));
        Assert.Equal("1000", cut.Find(".imp-table input[aria-label='Monthly Rental / Contract of row 1']").GetAttribute("value"));
        Assert.Empty(cut.FindAll("select[aria-label^='Lease type']"));
        Assert.Equal("900", cut.Find(".imp-table input[aria-label='Actual Mo. Rental of row 1']").GetAttribute("value"));
    }

    [Theory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    public void ImportOnlyNewMonthlyRowsDefaultDatesAndUploadsKeepMissingDatesInvalid(ObligationKind kind)
    {
        var cut = RenderComponent<SpaceHolderImport>(p => p.Add(x => x.Kind, kind));
        cut.Find(".imp-enter-manually").Click();
        cut.Find("input[aria-label='Effectivity Date of row 1']").Input("2026-03-17");
        cut.Find(".imp-add-row").Click();
        cut.Find(".imp-add-row").Click();
        Assert.Equal("2026-03-17", cut.Find("input[aria-label='Effectivity Date of row 1']").GetAttribute("value"));
        Assert.All(new[] { 2, 3 }, i => Assert.Equal(PhilippineTime.Today.ToString("yyyy-MM-dd"), cut.Find($"input[aria-label='Effectivity Date of row {i}']").GetAttribute("value")));
        cut.FindAll(".imp-review-actions button").Single(x => x.TextContent == "Discard").Click();
        var csv = "Actual Occupant,Occupancy basis,Effectivity Date,Approved amount\r\nHistorical holder,No contract (space only),,500\r\n";
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "missing-date.csv"));
        Assert.Equal("", cut.Find("input[aria-label='Effectivity Date of row 1']").GetAttribute("value"));
        Assert.Equal("Invalid", cut.Find(".shi-chip-status").TextContent);
        Assert.Contains("Choose the effectivity date", cut.Find("[popover='auto']").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportUnavailableOrMalformedPreviewNeverInventsNumberHints(bool malformed)
    {
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
            Task.FromResult(malformed
                ? Result<SpaceHolderImportPreview>.Success(NumberPreview(request, "99") with { Rows = NumberPreview(request, "99").Rows.Select(x => x with { RowNumber = 1 }).ToArray() })
                : Result<SpaceHolderImportPreview>.Failure("offline")));
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        Assert.Equal("Auto · pending", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder"));
        Assert.True(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled"));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("server check is unavailable", cut.Find(".shi-foot-note").TextContent);
            Assert.Equal("Auto", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder"));
            Assert.Equal("", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("value"));
        }, Timeout);
    }

    [Fact]
    public void ImportFloatingReasonKeepsExactServerMessageAndAccessibleInvoker()
    {
        const string message = "The server's exact duplicate explanation, including its recorded source.";
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
            Task.FromResult(Result<SpaceHolderImportPreview>.Success(NumberPreview(request, "4") with {
                Rows = NumberPreview(request, "4").Rows.Select((row, i) => i == 1 ? row with { Status = SpaceHolderImportStatus.Invalid, Code = "DuplicateSpace", Message = message } : row).ToArray() })));
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() =>
        {
            var popup = cut.Find("[role='tooltip'][popover='auto']");
            Assert.Equal(message, popup.QuerySelector("p")!.TextContent);
            var trigger = cut.Find("button.is-trigger");
            Assert.Equal("button", trigger.GetAttribute("type"));
            Assert.Equal(popup.Id, trigger.GetAttribute("popovertarget"));
            Assert.Equal(popup.Id, trigger.GetAttribute("aria-controls"));
            Assert.Equal(popup.Id, trigger.GetAttribute("aria-describedby"));
            Assert.Contains("row 2", trigger.GetAttribute("aria-label"));
            Assert.False(trigger.HasAttribute("title"));
            Assert.Empty(cut.FindAll(".imp-td-status .shi-why"));
        }, Timeout);
    }

    [Fact]
    public async Task ImportNumberHintsRefreshAfterExplicitEditsAndRemovalWithoutBecomingSuppliedNumbers()
    {
        ImportSpaceHoldersRequest? saved = null;
        var removalStarted = new TaskCompletionSource<ImportSpaceHoldersRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removalReply = new TaskCompletionSource<Result<SpaceHolderImportPreview>>();
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
        {
            if (request.Rows.Count == 2) { removalStarted.TrySetResult(request); return removalReply.Task; }
            return Task.FromResult(Result<SpaceHolderImportPreview>.Success(NumberPreview(request, request.Rows.Any(x => x.Account.SubjectLabel == "7") ? "8" : "3")));
        });
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(2, 0, [])));
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.Find("input[aria-label='Rent / Space No. of row 1']").Input("7");
        cut.WaitForAssertion(() => Assert.Equal("Auto · 8", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder")), Timeout);
        Assert.Equal("7", cut.Find("input[aria-label='Rent / Space No. of row 1']").GetAttribute("value"));
        cut.Find("button[aria-label='Remove row 1']").Click();
        var removal = await removalStarted.Task.WaitAsync(Timeout);
        cut.WaitForAssertion(() => Assert.Equal("Auto · pending", cut.Find("input[aria-label='Rent / Space No. of row 2']").GetAttribute("placeholder")), Timeout);
        await cut.InvokeAsync(() => removalReply.SetResult(Result<SpaceHolderImportPreview>.Success(NumberPreview(removal, "3"))));
        cut.WaitForAssertion(() => Assert.Equal("Auto · 3", cut.Find("input[aria-label='Rent / Space No. of row 2']").GetAttribute("placeholder")), Timeout);
        SubmitReadyImport(cut);
        Assert.Equal(new[] { "2", "" }, saved!.Rows.Select(x => x.Account.SubjectLabel));
    }

    [Fact]
    public async Task ImportBusyAndRetryKeepOneSubmissionIdentity()
    {
        var pending = new TaskCompletionSource<Result<ImportSpaceHoldersResult>>();
        var requests = new List<ImportSpaceHoldersRequest>();
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => requests.Add(r)).Returns(pending.Task);
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() => Assert.False(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled")), Timeout);
        var save = cut.Find(".imp-foot-bar .imp-btn-primary").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => Assert.True(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled")), Timeout);
        await cut.Find(".imp-foot-bar .imp-btn-primary").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Single(requests);
        pending.SetResult(Result<ImportSpaceHoldersResult>.Failure("not confirmed"));
        await save;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => requests.Add(r))
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(3, 0, [])));
        SubmitReadyImport(cut);
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].ClientOperationId, requests[1].ClientOperationId);
    }

    [Fact]
    public void ImportEditorPreservesEachRowsValuesWhenSwitchingAndRemoving()
    {
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.Find("button[aria-label='Edit row details 1']").Click();
        Assert.Contains("Row 1", cut.Find(".eemo-drawer-header-title").TextContent);
        cut.Find("input[aria-label='Contract reference of row 1']").Input("LC-EDITED");
        cut.Find(".shi-editor input[aria-label='Actual Mo. Rental of row 1']").Input("1700");
        Assert.Equal("20400.00", cut.Find("input[aria-label='Whole Year Rental of row 1']").GetAttribute("value"));
        Assert.True(cut.Find("input[aria-label='Whole Year Rental of row 1']").HasAttribute("readonly"));
        Assert.True(cut.Find("input[aria-label='Delinquent (₱) of row 1']").HasAttribute("readonly"));
        Assert.Equal(string.Empty, cut.Find("input[aria-label='Delinquent (₱) of row 1']").GetAttribute("value"));
        cut.FindAll("footer button").Single(x => x.TextContent == "Next row").Click();
        Assert.Contains("Jose Ramirez", cut.Find(".eemo-drawer-header-title").TextContent);
        Assert.False(cut.Find("input[aria-label='Contract reference of row 2']").HasAttribute("readonly"));
        cut.Find(".shi-editor input[aria-label='Actual Occupant (Leasee) of row 2']").Input("Jose Edited");
        cut.FindAll("footer button").Single(x => x.TextContent == "Previous row").Click();
        Assert.Equal("LC-EDITED", cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value"));
        cut.FindAll("footer button").Single(x => x.TextContent == "Back to review").Click();
        Assert.Empty(cut.FindAll("[role='dialog']"));
        cut.Find("button[aria-label='Remove row 1']").Click();
        Assert.Equal(2, cut.FindAll(".imp-table tbody tr").Count);
        Assert.Equal("Jose Edited", cut.Find("input[aria-label='Actual Occupant (Leasee) of row 1']").GetAttribute("value"));
        Assert.Equal("Ana Lopez", cut.Find("input[aria-label='Actual Occupant (Leasee) of row 2']").GetAttribute("value"));
    }

    [Fact]
    public void ImportEditorRevalidatesTermAndShowsReasonBesideTheField()
    {
        ImportSpaceHoldersRequest? previewed = null;
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
        {
            previewed = request;
            return Task.FromResult(Result<SpaceHolderImportPreview>.Success(new(request.Rows.Select((row, i) =>
                new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.Ready, null, null)).ToArray(), true)));
        });
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.Find("button[aria-label='Edit row details 1']").Click();
        cut.Find(".imp-table input[aria-label='No. of Years of row 1']").Input("0");
        var field = cut.Find(".shi-editor input[aria-label='No. of Years of row 1']");
        Assert.Equal("true", field.GetAttribute("aria-invalid"));
        Assert.Contains("1 to 50", field.ParentElement!.QuerySelector(".form-error")!.TextContent);
        Assert.Contains("1 invalid", cut.Find(".imp-review-detail").TextContent);
        cut.Find(".imp-table input[aria-label='No. of Years of row 1']").Input("2");
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, previewed!.Rows[0].Account.ContractYears);
            Assert.Contains("3 ready", cut.Find(".shi-foot-note").TextContent);
            Assert.Empty(cut.FindAll(".form-error, .shi-editor-error"));
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

    [Theory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    [InlineData(ObligationKind.FiestaArawLotRental)]
    public void ImportOtherServerReviewStatesRemainVisibleAndAreNeverSubmitted(ObligationKind kind)
    {
        ImportSpaceHoldersRequest? saved = null;
        _api.Setup(x => x.PreviewSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Returns((ImportSpaceHoldersRequest request) =>
            Task.FromResult(Result<SpaceHolderImportPreview>.Success(new(request.Rows.Select((row, i) =>
                new SpaceHolderImportRowResult(i + 1, i == 0 ? SpaceHolderImportStatus.NeedsSourceHolder : SpaceHolderImportStatus.Ready,
                    i == 0 ? "RequiresSourceHolder" : null, i == 0 ? "Confirm the source holder." : null)).ToArray(), false))));
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(2, 0, [])));
        var cut = RenderComponent<SpaceHolderImport>(p => p.Add(x => x.Kind, kind));
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 ready · 0 invalid · 1 need review", cut.Find(".imp-review-detail").TextContent);
            Assert.Equal("Needs holder", cut.Find(".shi-chip-status").TextContent);
            Assert.Equal("Review", cut.Find(".shi-row-edit").TextContent.Trim());
        }, Timeout);
        SubmitReadyImport(cut);
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "Jose Ramirez", "Ana Lopez" }, saved!.Rows.Select(x => x.Account.ActualOccupant));
            if (kind == ObligationKind.FiestaArawLotRental)
                Assert.All(saved.Rows, row =>
                {
                    Assert.NotNull(row.Account.EventDate);
                    Assert.Null(row.Account.Arrangement);
                    Assert.Null(row.Account.ContractYears);
                    Assert.Null(row.Account.ContractMonthlyRate);
                });
        }, Timeout);
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
            Assert.Equal(new[] { "#", "Actual Occupant (Leasee)", "Name on Contract", "Rent / Space No.", "Effectivity Date", "No. of Years", "Area (sqm)", "Monthly Rental / Contract", "Actual Mo. Rental", "Whole Year Rental", "Area Loc.", "Status", "Actions" },
                cut.FindAll(".imp-table thead th").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).ToArray());
            Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count);
            Assert.Empty(cut.FindAll(".shi-inline-status, .shi-why"));
            Assert.Equal(3, cut.FindAll(".shi-row-edit").Count);
            Assert.All(cut.FindAll(".shi-row-edit"), action => Assert.Equal("Details", action.TextContent.Trim()));
            Assert.All(cut.FindAll(".shi-chip-status"), status => Assert.Equal("Ready", status.TextContent));
            Assert.Contains("3 ready", cut.Find(".shi-foot-note").TextContent);
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
        }, Timeout);
        cut.Find("button[aria-label='Edit row details 1']").Click();
        Assert.Equal("LC-1", cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value"));
        cut.Find(".eemo-drawer-header-close").Click();
        cut.Find("button[aria-label='Edit row details 2']").Click();
        Assert.False(cut.Find("input[aria-label='Contract reference of row 2']").HasAttribute("readonly"));
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
            var popup = cut.FindAll(".imp-table tbody tr")[1].QuerySelector("[popover='auto'][role='tooltip']")!;
            Assert.Contains("This space already has an account.", popup.TextContent);
            Assert.Empty(cut.FindAll(".imp-td-status .shi-why"));
            Assert.Equal("Review", cut.FindAll(".imp-table tbody tr")[1].QuerySelector(".shi-row-edit")!.TextContent.Trim());
            Assert.Contains("Auto · 3", cut.Find("input[aria-label='Rent / Space No. of row 3']").GetAttribute("placeholder"));
        }, Timeout);

        SubmitReadyImport(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "1", string.Empty }, saved!.Rows.Select(x => x.Account.SubjectLabel).ToArray());   // a blank number stays blank: the server numbers it
            Assert.All(saved.Rows, x => { Assert.Equal(Guid.Empty, x.Account.PayorId); Assert.False(string.IsNullOrWhiteSpace(x.Account.ActualOccupant)); });
            Assert.Contains("active", cut.FindAll(".imp-step")[2].ClassList);
            Assert.Contains("2 rent / space holders imported", cut.Find(".imp-state-title").TextContent);
        }, Timeout);
    }

    [Fact]
    public void ImportManualContractWorkflowNeverFabricatesDocumentEvidence()
    {
        var cut = RenderComponent<VegetableFruitImport>();
        cut.Find(".imp-enter-manually").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".imp-table tbody tr")), Timeout);

        cut.Find("input[aria-label='Actual Occupant (Leasee) of row 1']").Input("Ana Reyes");
        cut.Find("button[aria-label='Edit row details 1']").Click();
        Assert.False(cut.Find("input[aria-label='Contract reference of row 1']").HasAttribute("readonly"));
        Assert.Equal("", cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value"));
        Assert.Empty(cut.FindAll("select[aria-label^='Lease type']"));
        cut.Find("input[aria-label='Contract reference of row 1']").Input("LC-2026-014");
        cut.WaitForAssertion(() =>
        {
            var reference = cut.Find("input[aria-label='Contract reference of row 1']");
            Assert.False(reference.HasAttribute("readonly"));
            Assert.Equal("LC-2026-014", reference.GetAttribute("value"));
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

        cut.Find("button[aria-label='Edit row details 1']").Click();
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
            Assert.Contains("1 ready", cut.Find(".shi-foot-note").TextContent);
            Assert.Empty(cut.FindAll(".shi-why, .shi-inline-status"));
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

        SubmitReadyImport(cut);

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
    [InlineData("51", "Years must be a whole number from 1 to 50")]
    [InlineData("0", "Years must be a whole number from 1 to 50")]
    public void Import_UnsupportedKanmanggayTermsAreInvalid_NeverSilentlyChanged(string years, string reason)
    {
        var cut = RenderComponent<KanmanggayImport>();
        var csv = OfficeRegisterHeader + $"Ana Reyes,Estate of Ana Reyes,K-1,2026-09-04,{years},9.5,1000,900,\r\n";

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "register.csv"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(reason, cut.Find(".imp-table tbody tr [popover='auto']").TextContent);
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
        var csv = "Name of Lessee (Actual Occupants),Name of Lessee per Signed Contract,Effectivity of Lessee Contract,No. of Years,Area,Actual Monthly Rentals,Occupancy basis\r\n" +
                  "Lina Torres,Lina Torres,2026-08-10,1,6,500,Signed lease contract\r\nRoy Dela Cruz,,2026-08-01,,,400,No contract (space only)\r\n";
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "vf.csv"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".imp-table tbody tr").Count), Timeout);
        Assert.All(cut.FindAll("input[aria-label^='Rent / Space No.']"), cell => Assert.True(cell.HasAttribute("readonly")));                                       // Vegetable / Fruits numbers are the server's

        SubmitReadyImport(cut);

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
    public void VegetableFruitManualRowsUseContractWorkflowWithoutInventingContractDocuments()
    {
        var cut = RenderComponent<VegetableFruitImport>();
        cut.Find(".imp-enter-manually").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".imp-table tbody tr")), Timeout);
        Assert.Empty(cut.FindAll(".imp-table select[aria-label^='Lease type']"));
        cut.Find("button[aria-label='Edit row details 1']").Click();
        Assert.Empty(cut.FindAll(".shi-editor select[aria-label^='Lease type']"));
        Assert.Equal("", cut.Find("input[aria-label='Contract reference of row 1']").GetAttribute("value"));
        cut.Find(".eemo-drawer-header-close").Click();

        cut.Find(".imp-add-row").Click();
        cut.Find(".imp-add-row").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count);
            Assert.Empty(cut.FindAll(".imp-table select[aria-label^='Lease type']"));
        }, Timeout);
        cut.Find("button[aria-label='Edit row details 3']").Click();
        Assert.Equal("3", cut.Find(".imp-table input[aria-label='No. of Years of row 3']").GetAttribute("value"));
        Assert.False(cut.Find("input[aria-label='Name on Contract of row 3']").HasAttribute("readonly"));
        cut.Find(".eemo-drawer-header-close").Click();
        cut.Find("button[aria-label='Edit row details 2']").Click();
        Assert.Equal("3", cut.Find(".imp-table input[aria-label='No. of Years of row 2']").GetAttribute("value"));
        Assert.False(cut.Find("input[aria-label='Name on Contract of row 2']").HasAttribute("readonly"));
        Assert.Contains("Add row with contract", cut.Find(".imp-add-row").TextContent);
        Assert.Empty(cut.FindAll(".imp-add-space-row"));
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
            Assert.Equal(new[] { "#", "Event", "Actual Occupant (Leasee)", "Rent / Space No.", "Event date", "Approved Event Amount (₱)", "Status", "Actions" },
                cut.FindAll(".imp-table thead th").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).ToArray());
            Assert.Equal("Fiesta", cut.Find("select[aria-label='Event of row 1']").GetAttribute("value"));
            Assert.Equal("Araw", cut.Find("select[aria-label='Event of row 3']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("select[aria-label^='Occupancy basis']"));                                          // basis is Kanmanggay's, not a lot rental's
        }, Timeout);
    }

    [Fact]
    public void FiestaArawCsvTemplateHeadersPreserveTheApprovedOneTimeAmount()
    {
        ImportSpaceHoldersRequest? saved = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>())).Callback<ImportSpaceHoldersRequest>(r => saved = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(1, 0, [])));
        var cut = RenderComponent<FiestaArawImport>();
        var template = cut.Find(".imp-upload-foot a[download]").GetAttribute("href")!;
        var csv = Uri.UnescapeDataString(template[(template.IndexOf(',') + 1)..]) + "Ana Reyes,,Fiesta,2026-08-15,2500\r\n";
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "event-template.csv"));
        Assert.Contains("1 ready", cut.Find(".imp-review-detail").TextContent);
        SubmitReadyImport(cut);
        cut.WaitForAssertion(() =>
        {
            var account = Assert.Single(saved!.Rows).Account;
            Assert.Equal(2500m, account.Amount);
            Assert.Equal(LotRentalEvent.Fiesta, account.Event);
            Assert.Equal(new DateOnly(2026, 8, 15), account.EventDate);
            Assert.Null(account.ContractYears);
            Assert.Null(account.ContractMonthlyRate);
            Assert.Null(account.Arrangement);
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
