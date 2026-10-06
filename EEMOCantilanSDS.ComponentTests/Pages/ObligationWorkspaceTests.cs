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
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find("[aria-label='Account holders found']").TextContent), Timeout);
        cut.Find("[aria-label='Account holders found'] button").Click();          // an explicit choice of the Payor the server returned
        cut.Find("#obw-subject").Change("Space K-4");
        cut.Find("input[placeholder='As written on the contract']").Change("LC-2026-014");
        cut.Find("#obw-amount").Change("1200");
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
            var titles = drawer.QuerySelectorAll(".avm-section-label").Select(x => x.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "Occupant Information", "Space & Contract Details" }, titles);
            var basis = drawer.QuerySelectorAll("[role='radio']").Select(x => x.QuerySelector(".avm-choice-title")!.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "Signed lease contract", "No contract (space only)" }, basis);
            Assert.Contains("Contract Effectivity (month)", drawer.TextContent);                         // a month, not a date
            Assert.NotNull(drawer.QuerySelector("input[placeholder='As written on the contract']"));
            Assert.Equal("Kanmanggay", drawer.QuerySelector(".avm-input-locked")!.TextContent.Trim());   // the operation is context, not a choice
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
        cut.WaitForAssertion(() => cut.Find("[role='dialog'] input[type='search']"), Timeout);
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("No contract")).Click();
        var search = cut.Find("[role='dialog'] input[type='search']");
        search.Input("Ana Reyes");
        search.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "s" });
        cut.WaitForAssertion(() => cut.Find("[aria-label='Account holders found'] button"), Timeout);
        cut.Find("[aria-label='Account holders found'] button").Click();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find(".obw-selected").TextContent), Timeout);
        cut.Find("#obw-subject").Change("K-7");
        cut.Find("#obw-amount").Change("900");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((PayorId, "K-7", 900m, OccupancyArrangement.SpaceOnly, (string?)null),
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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[aria-label='Kanmanggay accounts'] tbody tr")), Timeout);
        cut.FindAll(".filter-tab").Single(t => t.TextContent.Trim() == "Paid").Click();            // hides the (partly paid) existing holder
        cut.WaitForAssertion(() => Assert.DoesNotContain("Space K-4", cut.Find("[aria-label='Kanmanggay accounts'] tbody").TextContent), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New").Click();
        var search = cut.Find("[role='dialog'] input[type='search']");
        search.Input("Ana Reyes");
        search.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "s" });
        cut.WaitForAssertion(() => cut.Find("[aria-label='Account holders found'] button"), Timeout);
        cut.Find("[aria-label='Account holders found'] button").Click();
        cut.Find("#obw-amount").Change("900");
        cut.Find("form[aria-label='Open account']").Submit();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Kanmanggay accounts'] tbody tr").Select(r => r.TextContent).ToArray();
            Assert.Contains(rows, r => r.Contains("Space K-9"));
            Assert.Contains(rows, r => r.Contains("Space K-4"));
            Assert.Equal("All", cut.Find(".filter-tab.active").TextContent.Trim());
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
            Assert.Equal(new[] { "Event & Occupant Information", "Lot & Event Details" }, drawer.QuerySelectorAll(".avm-section-label").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Equal(new[] { "Fiesta", "Araw" }, drawer.QuerySelectorAll(".avm-choice-item .avm-choice-title").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Empty(drawer.QuerySelectorAll("select"));                                          // an explicit choice, not a dropdown
            Assert.NotNull(drawer.QuerySelector(".eemo-drawer-footer"));
        }, Timeout);

        cut.FindAll("[role='radio']").Single(b => b.TextContent.Contains("Araw")).Click();
        var search = cut.Find("[role='dialog'] input[type='search']");
        search.Input("Ana Reyes");
        search.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "s" });
        cut.WaitForAssertion(() => cut.Find("[aria-label='Account holders found'] button"), Timeout);
        cut.Find("[aria-label='Account holders found'] button").Click();
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
    public void ImportList_IsOfferedFromTheWorkspace_AsALinkToItsOwnPage()
    {
        Serve(ObligationKind.KanmanggaySpaceRental);
        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
            Assert.Equal("/operations/kanmanggay/import", cut.FindAll("a").Single(a => a.TextContent.Trim() == "Import list").GetAttribute("href")), Timeout);
    }

    [Fact]
    public void Import_IsTheStallholderImportFamily_HeroThreeStepsUploadTemplateSampleAndManual_WithNoPasteBox()
    {
        var cut = RenderComponent<KanmanggayImport>();

        Assert.Equal("KANMANGGAY · BULK IMPORT", cut.Find(".imp-hero-eyebrow").TextContent.Trim().ToUpperInvariant());
        Assert.Equal("Import Kanmanggay Space Holders", cut.Find(".imp-hero-title").TextContent.Trim());
        Assert.Equal(new[] { "1 Upload", "2 Review & edit", "3 Save" }, cut.FindAll(".imp-step").Select(x => x.TextContent.Trim()).ToArray());
        Assert.Contains("active", cut.Find(".imp-step").ClassList);
        Assert.NotNull(cut.Find(".imp-drop input[type='file']"));
        Assert.Contains("Download CSV template", cut.Find(".imp-upload-foot").TextContent);
        Assert.NotNull(cut.Find(".imp-use-sample"));
        Assert.NotNull(cut.Find(".imp-enter-manually"));
        Assert.Empty(cut.FindAll("textarea"));                                        // no pasted-rows box: upload, sample or manual, as in ICE
        Assert.Equal(new[] { "Space No.", "Actual Occupant", "Occupancy basis", "Contract reference", "Contract Effectivity (month)", "Approved monthly rental", "Closed on" },
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
            Assert.Equal(new[] { "#", "Space No.", "Actual Occupant", "Occupancy basis", "Contract reference", "Contract Effectivity (month)", "Approved monthly rental", "Closed on", "Status" },
                cut.FindAll(".imp-table thead th").Select(x => x.TextContent.Trim()).Where(x => x.Length > 0).ToArray());
            Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count);
            Assert.All(cut.FindAll(".imp-table tbody tr"), r => Assert.Contains("Needs Payor", r.TextContent));        // a sample never carries a Payor
            Assert.NotNull(cut.Find(".imp-add-row"));
            Assert.Equal(new[] { "Cancel", "Import 0 ready rows" }, cut.FindAll(".imp-foot-bar .imp-btn").Select(x => x.TextContent.Trim()).ToArray());
            Assert.True(cut.Find(".imp-foot-bar .imp-btn-primary").HasAttribute("disabled"));
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
    public void Import_APayorIsChosenFromACompactRowAction_AndNeverFromTheName()
    {
        _collections.Setup(x => x.SearchCollectionPayorsAsync("Maria Santos")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success(new[] { new CollectionPayorDto(PayorId, "Maria Santos", ["NPM · 4"]) }));
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-use-sample").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".imp-table tbody tr").Count), Timeout);

        cut.FindAll(".imp-table tbody tr")[0].QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Link Payor").Click();

        cut.WaitForAssertion(() => Assert.Equal("Maria Santos", cut.Find("[role='dialog'] input[type='search']").GetAttribute("value")), Timeout);
        _collections.Verify(x => x.SearchCollectionPayorsAsync(It.IsAny<string>()), Times.Never);                   // pre-filled, never searched for the office
        cut.Find("form[aria-label='Find an account holder']").Submit();
        cut.WaitForAssertion(() => Assert.Contains("NPM · 4", cut.Find("[aria-label='Account holders found']").TextContent), Timeout);
        Assert.True(cut.FindAll("[role='dialog'] footer button").Single(b => b.TextContent.Trim() == "Use Payor").HasAttribute("disabled"));
        cut.Find("[aria-label='Account holders found'] button").Click();
        cut.FindAll("[role='dialog'] footer button").Single(b => b.TextContent.Trim() == "Use Payor").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[role='dialog']"));
            var row = cut.FindAll(".imp-table tbody tr")[0].TextContent;
            Assert.Contains("Maria Santos", row);
            Assert.Contains("Ready", row);
            Assert.Contains("1 ready", cut.Find(".shi-foot-note").TextContent);
        }, Timeout);
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
                if (account.PayorId == Guid.Empty)
                    return new SpaceHolderImportRowResult(i + 1, SpaceHolderImportStatus.NeedsPayor, "RequiresPayor", "Choose an account holder.", Facts:
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
        // Sample rows: 1 Maria (signed), 2 Jose (space only), and a third with no number. Each gets the office's explicit Payor.
        for (var i = 0; i < 3; i++)
        {
            cut.WaitForAssertion(() => cut.FindAll(".imp-table tbody tr")[i].QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Link Payor"), Timeout);
            cut.FindAll(".imp-table tbody tr")[i].QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Link Payor").Click();
            cut.WaitForAssertion(() => cut.Find("[role='dialog'] form[aria-label='Find an account holder']"), Timeout);
            cut.Find("[role='dialog'] form[aria-label='Find an account holder']").Submit();
            cut.WaitForAssertion(() => cut.Find("[aria-label='Account holders found'] button"), Timeout);
            cut.Find("[aria-label='Account holders found'] button").Click();
            cut.FindAll("[role='dialog'] footer button").Single(b => b.TextContent.Trim() == "Use Payor").Click();
            cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")), Timeout);
        }

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 ready", cut.Find(".shi-foot-note").TextContent);
            Assert.Contains("1 invalid", cut.Find(".shi-foot-note").TextContent);
            Assert.Contains("This space already has an account.", cut.FindAll(".imp-table tbody tr")[1].TextContent);     // the server's reason, as given
            Assert.Contains("Auto · 3", cut.Find("input[aria-label='Space No. of row 3']").GetAttribute("placeholder"));
        }, Timeout);

        cut.Find(".imp-foot-bar .imp-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "1", string.Empty }, saved!.Rows.Select(x => x.Account.SubjectLabel).ToArray());   // a blank number stays blank: the server numbers it
            Assert.All(saved.Rows, x => Assert.Equal(PayorId, x.Account.PayorId));
            Assert.Contains("active", cut.FindAll(".imp-step")[2].ClassList);
            Assert.Contains("2 space holders imported", cut.Find(".imp-state-title").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Import_NeverAssumesTheBasis_AndASignedRowSendsItsContractReference()
    {
        var cut = RenderComponent<KanmanggayImport>();
        cut.Find(".imp-enter-manually").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".imp-table tbody tr")), Timeout);

        cut.Find("input[aria-label='Actual Occupant of row 1']").Input("Ana Reyes");
        Assert.True(cut.Find("input[aria-label='Contract reference of row 1']").HasAttribute("readonly"));              // no basis chosen yet: nothing to reference

        cut.Find("select[aria-label='Occupancy basis of row 1']").Change("Signed lease contract");
        cut.WaitForAssertion(() => Assert.False(cut.Find("input[aria-label='Contract reference of row 1']").HasAttribute("readonly")), Timeout);
        cut.Find("input[aria-label='Contract reference of row 1']").Input("LC-2026-014");

        cut.Find("select[aria-label='Occupancy basis of row 1']").Change("No contract (space only)");
        cut.WaitForAssertion(() =>
        {
            var reference = cut.Find("input[aria-label='Contract reference of row 1']");
            Assert.True(reference.HasAttribute("readonly"));                                                             // a space let without a contract carries none
            Assert.Equal(string.Empty, reference.GetAttribute("value") ?? string.Empty);
        }, Timeout);
    }

    [Fact]
    public void FiestaArawImport_IsTheSameFamily_RowsNameTheirEvent_AndNeverMixThem()
    {
        var cut = RenderComponent<FiestaArawImport>();
        Assert.Equal("Import Fiesta / Araw Lot Holders", cut.Find(".imp-hero-title").TextContent.Trim());
        Assert.Empty(cut.FindAll("textarea"));

        cut.Find(".imp-use-sample").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "#", "Lot No.", "Actual Occupant", "Event", "Event date", "Approved lot amount", "Closed on", "Status" },
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
}
