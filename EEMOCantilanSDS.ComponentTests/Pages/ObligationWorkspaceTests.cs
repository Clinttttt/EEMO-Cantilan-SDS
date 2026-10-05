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
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find("[aria-label='Business Payors found']").TextContent), Timeout);
        cut.Find("[aria-label='Business Payors found'] button").Click();          // an explicit choice of the Payor the server returned
        cut.FindAll("form[aria-label='Open account'] input[type='text']")[0].Change("Space K-4");
        cut.FindAll("form[aria-label='Open account'] input[type='text']")[1].Change("LC-2026-014");
        cut.Find("form[aria-label='Open account'] input[type='number']").Change("1200");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((ObligationKind.KanmanggaySpaceRental, PayorId, "Space K-4", 1200m),
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
    public void AddNew_IsGroupedIntoSections_WithAnOccupancyBasisChoice_AndNoFabricatedContractFields()
    {
        Serve(ObligationKind.KanmanggaySpaceRental, Space());

        var cut = RenderComponent<Kanmanggay>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            var titles = drawer.QuerySelectorAll(".ans-title").Select(x => x.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "Occupancy", "Business Payor", "Space details", "Contract details" }, titles);
            var basis = drawer.QuerySelectorAll("[role='radio']").Select(x => x.QuerySelector(".cc-title")!.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "Signed lease contract", "No contract (space only)" }, basis);
            Assert.Contains("Contract effectivity", drawer.TextContent);
            Assert.Contains("Contract reference", drawer.TextContent);
        }, Timeout);

        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("No contract")).Click();

        cut.WaitForAssertion(() =>
        {
            var drawer = Assert.Single(cut.FindAll("[role='dialog']"));
            Assert.Equal("Rental details", drawer.QuerySelectorAll(".ans-title").Last().TextContent.Trim());
            Assert.Contains("Occupying since", drawer.TextContent);
            Assert.DoesNotContain("Contract effectivity", drawer.TextContent);
            Assert.DoesNotContain("Contract reference", drawer.TextContent);         // no contract is implied for a space-only occupancy
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
        cut.WaitForAssertion(() => cut.Find("[role='dialog'] input[type='search']"), Timeout);
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("No contract")).Click();
        var search = cut.Find("[role='dialog'] input[type='search']");
        search.Input("Ana Reyes");
        search.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "s" });
        cut.WaitForAssertion(() => cut.Find("[aria-label='Business Payors found'] button"), Timeout);
        cut.Find("[aria-label='Business Payors found'] button").Click();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find(".obw-selected").TextContent), Timeout);
        cut.Find("[role='dialog'] input[type='text']").Change("K-7");
        cut.Find("[role='dialog'] input[type='number']").Change("900");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((PayorId, "K-7", 900m, OccupancyArrangement.SpaceOnly, (string?)null),
                (sent!.PayorId, sent.SubjectLabel, sent.Amount, sent.Arrangement, sent.ContractReference));
        }, Timeout);
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
            Assert.Equal(new[] { "Event", "Business Payor", "Lot details", "Rental details" }, drawer.QuerySelectorAll(".ans-title").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Equal(new[] { "Fiesta", "Araw" }, drawer.QuerySelectorAll(".cc-item .cc-title").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Empty(drawer.QuerySelectorAll("select"));                                          // an explicit choice, not a dropdown
            Assert.NotNull(drawer.QuerySelector(".sd-footer"));
        }, Timeout);

        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("Araw")).Click();
        var search = cut.Find("[role='dialog'] input[type='search']");
        search.Input("Ana Reyes");
        search.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "s" });
        cut.WaitForAssertion(() => cut.Find("[aria-label='Business Payors found'] button"), Timeout);
        cut.Find("[aria-label='Business Payors found'] button").Click();
        cut.Find("[role='dialog'] input[type='number']").Change("2500");
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
    public void ImportList_IsOfferedFromTheWorkspace_AsALinkToItsOwnPage()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
            Assert.Equal("/operations/kanmanggay/import", cut.FindAll("a").Single(a => a.TextContent.Trim() == "Import list").GetAttribute("href")), Timeout);
    }

    [Fact]
    public void Import_IsAThreeStepFlow_WithUploadManualEntryAndATemplate_AndTheReviewStageHasNoPasteBox()
    {
        var cut = RenderComponent<KanmanggayImport>();

        Assert.Equal("KANMANGGAY · BULK IMPORT", cut.Find(".shi-eyebrow").TextContent.Trim().ToUpperInvariant());
        Assert.Equal("Import Kanmanggay Space Holders", cut.Find("h1").TextContent.Trim());
        Assert.Equal(new[] { "1Upload / Add rows", "2Review & edit", "3Save" }, cut.FindAll(".shi-step").Select(x => x.TextContent.Trim()).ToArray());
        Assert.Equal("1Upload / Add rows", cut.Find(".shi-step[aria-current='true']").TextContent.Trim());
        Assert.NotNull(cut.Find("input[type='file']"));
        Assert.Contains("Download CSV template", cut.Markup);
        Assert.Empty(cut.FindAll("textarea"));                                     // the textarea is a secondary way in, not the page
        Assert.Contains("Occupancy basis", cut.Find(".shi-chips").TextContent);

        cut.Find(".shi-enter-manually").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2Review & edit", cut.Find(".shi-step[aria-current='true']").TextContent.Trim());
            Assert.Single(cut.FindAll("[aria-label='Rows to import'] tbody tr"));
            Assert.Contains("1 Invalid", cut.Find(".shi-summary").TextContent);
            Assert.Equal(new[] { "#", "Space No.", "Actual occupant", "Business Payor", "Occupancy basis", "Contract reference", "Start / effectivity", "Approved monthly", "Closed on", "Status" },
                cut.FindAll("thead th").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0 && x != "Remove").ToArray());
            Assert.True(cut.Find(".shi-import-go").HasAttribute("disabled"));
        }, Timeout);

        cut.Find(".shi-add-row").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[aria-label='Rows to import'] tbody tr").Count), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Discard").Click();
        cut.WaitForAssertion(() => Assert.Equal("1Upload / Add rows", cut.Find(".shi-step[aria-current='true']").TextContent.Trim()), Timeout);
    }

    [Fact]
    public void Import_ReviewsEveryRow_NeverLinksAPayorByName_AndImportsOnlyTheReadyRows()
    {
        _collections.Setup(x => x.SearchCollectionPayorsAsync("Ana Reyes")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Ana Reyes") }));
        ImportSpaceHoldersRequest? sent = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>()))
            .Callback<ImportSpaceHoldersRequest>(r => sent = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(1, 0, ["Row 2: Needs Payor"])));

        var cut = RenderComponent<KanmanggayImport>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Paste from a spreadsheet").Click();
        // Space, Occupant, Monthly, Start, Closed, Basis, Contract reference
        cut.Find("textarea[aria-label='Spreadsheet rows']").Change("K-1\tAna Reyes\t900\t2026-09\t\tNo contract (space only)\t\nK-2\tBen Cruz\tabc\t2026-09\t\tSigned lease contract\tLC-2");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review rows").Click();

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("[aria-label='Import summary']").TextContent;
            Assert.Contains("0 Ready", summary);
            Assert.Contains("1 Needs Payor", summary);
            Assert.Contains("1 Invalid", summary);
            Assert.True(cut.Find(".shi-import-go").HasAttribute("disabled"));
        }, Timeout);

        cut.FindAll("[aria-label='Rows to import'] tbody tr")[0].QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Find Payor").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Use Ana Reyes · same name", cut.Markup);
            Assert.Contains("0 Ready", cut.Find("[aria-label='Import summary']").TextContent);           // a name match is only a candidate
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Use Ana Reyes")).Click();
        cut.WaitForAssertion(() => Assert.Contains("1 Ready", cut.Find("[aria-label='Import summary']").TextContent), Timeout);
        cut.Find(".shi-import-go").Click();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(sent!.Rows);                                                         // only the ready row is sent
            Assert.Equal((PayorId, "K-1", 900m), (row.Account.PayorId, row.Account.SubjectLabel, row.Account.Amount));
            Assert.Equal((OccupancyArrangement.SpaceOnly, (string?)null), (row.Account.Arrangement, row.Account.ContractReference));
            Assert.Equal("3Save", cut.Find(".shi-step[aria-current='true']").TextContent.Trim());
            Assert.Contains("1 space holder imported", cut.Markup);
            Assert.Contains("Row 2: Needs Payor", cut.Find("[aria-label='Rows that need review']").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Import_NeverAssumesTheBasis_AndASignedRowSendsItsContractReference()
    {
        ImportSpaceHoldersRequest? sent = null;
        _api.Setup(x => x.ImportSpaceHoldersAsync(It.IsAny<ImportSpaceHoldersRequest>()))
            .Callback<ImportSpaceHoldersRequest>(r => sent = r)
            .ReturnsAsync(Result<ImportSpaceHoldersResult>.Success(new(1, 0, [])));

        var cut = RenderComponent<KanmanggayImport>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Paste from a spreadsheet").Click();
        cut.Find("textarea[aria-label='Spreadsheet rows']").Change("K-9\tAna Reyes\t900\t2026-09-01\t");              // no basis stated
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review rows").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose an occupancy basis", cut.Find("[aria-label='Rows to import'] tbody").TextContent);
            Assert.Contains("1 Invalid", cut.Find("[aria-label='Import summary']").TextContent);
        }, Timeout);

        var select = cut.Find("select[aria-label='Occupancy basis of row 1']");
        select.Change("signed");
        cut.WaitForAssertion(() => Assert.False(cut.Find("input[aria-label='Contract reference of row 1']").HasAttribute("disabled")), Timeout);
        cut.Find("input[aria-label='Contract reference of row 1']").Change("LC-2026-014");
        cut.WaitForAssertion(() => Assert.Contains("1 Needs Payor", cut.Find("[aria-label='Import summary']").TextContent), Timeout);

        cut.Find("select[aria-label='Occupancy basis of row 1']").Change("space");
        cut.WaitForAssertion(() =>
        {
            var reference = cut.Find("input[aria-label='Contract reference of row 1']");
            Assert.True(reference.HasAttribute("disabled"));                                                         // a space let without a contract carries none
            Assert.Equal(string.Empty, reference.GetAttribute("value") ?? string.Empty);
        }, Timeout);
    }

    [Fact]
    public void FiestaArawImport_RowsNameTheirEvent_AndNeverMixThem()
    {
        var cut = RenderComponent<FiestaArawImport>();
        Assert.Equal("Import Fiesta / Araw Lot Holders", cut.Find("h1").TextContent.Trim());
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Paste from a spreadsheet").Click();
        cut.Find("textarea[aria-label='Spreadsheet rows']").Change("L-1\tAna Reyes\t2500\t2026-08-15\tAraw\t");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review rows").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Araw", cut.Find("select[aria-label='Event of row 1']").QuerySelector("option[selected]")!.TextContent.Trim());
            Assert.Empty(cut.FindAll("select[aria-label^='Occupancy basis']"));                                    // basis is Kanmanggay's, not a lot rental's
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
