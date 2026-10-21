using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class SpaceHolderWorkspaceTests : TestContext
{
    private readonly Mock<IObligationsApiClient> _api = new();
    public SpaceHolderWorkspaceTests()
    {
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ICollectionActivityApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<FacilityState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }
    private static ObligationAccountDto Account(string name = "Actual holder", bool closed = false, decimal collected = 0m) => new(
        Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental, "Kanmanggay", Guid.Empty, "Compatibility name", null, null,
        "007", null, null, new(2026, 1, 1), closed ? new(2026, 9, 30) : null, 900m, new(2026, 1, 1),
        900m, collected, 900m - collected, OccupancyArrangement.SpaceOnly, "Must not print", name);
    private void Serve(params ObligationAccountDto[] rows)
    {
        _api.Setup(x => x.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success(rows));
        _api.Setup(x => x.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental)).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new(rows, 1800m, 900m, 900m)));
    }
    private static void Click(IRenderedFragment cut, string label) => cut.FindAll("button").Single(x => x.TextContent.Trim() == label).Click();

    [Theory]
    [InlineData(typeof(Kanmanggay), ObligationKind.KanmanggaySpaceRental, "kanmanggay")]
    [InlineData(typeof(KanmanggayManageSpaceHolders), ObligationKind.KanmanggaySpaceRental, "kanmanggay")]
    [InlineData(typeof(VegetableFruit), ObligationKind.VegetableFruitSpaceRental, "vegetable-fruit")]
    [InlineData(typeof(VegetableFruitManageSpaceHolders), ObligationKind.VegetableFruitSpaceRental, "vegetable-fruit")]
    [InlineData(typeof(FiestaAraw), ObligationKind.FiestaArawLotRental, "fiesta-araw")]
    public void ImportLivesInRegistrationFooterAndNeverCreatesAnAccount(Type page, ObligationKind kind, string operation)
    {
        _api.Setup(x => x.GetAccountsAsync(kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([]));
        _api.Setup(x => x.GetWorkspaceAsync(kind)).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([], 0m, 0m, 0m)));
        Services.AddSingleton(Mock.Of<IGovernedServicesApiClient>());
        Services.AddSingleton(Mock.Of<IEcfCollectionsApiClient>());
        var cut = Render(builder => { builder.OpenComponent(0, page); builder.CloseComponent(); });
        Assert.DoesNotContain(cut.FindAll("button, a"), x => x.TextContent.Trim() == "Import list");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add New" || b.TextContent.Contains("Add Occupant")).Click();
        Assert.Equal(new[] { "Import list", "Add Rent / Space Holder" }, cut.FindAll("footer button").Select(x => x.TextContent.Trim()));   // no second dismiss beside the header X, and no import-history entry
        Assert.NotNull(cut.Find("#obw-occupant"));
        SaveVisualArtifact(operation + "-registration", cut.Markup);
        Click(cut, "Import list");
        Assert.EndsWith($"/operations/{operation}/import", Services.GetRequiredService<NavigationManager>().Uri);
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
        cut.Find(".eemo-drawer-header-close").Click();
        Assert.Empty(cut.FindAll("form[aria-label='Open account']"));
    }

    [Theory]
    [InlineData("Active", "pill-active")]
    [InlineData("Closed", "pill-inactive")]
    [InlineData("Paid", "pill-paid")]
    [InlineData("Partial", "pill-partial")]
    [InlineData("Unpaid", "pill-unpaid")]
    public void StatusPillsKeepTextAndSemanticTone(string status, string tone)
    {
        var cut = RenderComponent<AccountStatusPill>(p => p.Add(x => x.Status, status));
        Assert.Equal(status, cut.Find("span").TextContent);
        Assert.Contains(tone, cut.Find("span").ClassList);
    }

    [Theory]
    [InlineData("All", "All", 4)]
    [InlineData("All", "Unpaid", 2)]
    [InlineData("Active", "All", 2)]
    [InlineData("Active", "Partial", 1)]
    [InlineData("Closed", "All", 2)]
    [InlineData("Closed", "Paid", 1)]
    [InlineData("Closed", "Unpaid", 1)]
    public void LifecycleSegmentsAndPaymentFiltersCombineWithoutChangingTotals(string lifecycle, string payment, int count)
    {
        Serve(Account("Active unpaid"), Account("Active partial", collected: 400m), Account("Closed paid", true, 900m), Account("Closed unpaid", true));
        var cut = RenderComponent<KanmanggayManageSpaceHolders>();
        var before = cut.FindComponent<RentalRegistryHero>().Markup;
        var group = cut.Find("[aria-label='Account lifecycle']");
        Assert.Equal(new[] { "All", "Active", "Closed" }, group.QuerySelectorAll("option").Select(x => x.TextContent));
        group.Change(lifecycle);
        cut.FindAll("[aria-label='Payment status'] .filter-tab").Single(x => x.TextContent == payment).Click();
        Assert.Equal(count, cut.FindAll("tbody > tr .rental-registry-actions").Count);
        Assert.Equal(before, cut.FindComponent<RentalRegistryHero>().Markup);
        foreach (var row in cut.FindAll("tbody > tr").Where(x => x.QuerySelector(".rental-registry-actions") is not null))
        {
            var closed = row.TextContent.Contains("Closed");
            Assert.Equal(new[] { "View Details", "View Profile", "Payment History" }, row.QuerySelectorAll(".rental-registry-actions button").Where(x => !x.HasAttribute("disabled") && x.GetAttribute("title") is "View Details" or "View Profile" or "Payment History").Select(x => x.GetAttribute("title")));
        }
        cut.Find(".search-input").Input("No match");
        Assert.Equal(before, cut.FindComponent<RentalRegistryHero>().Markup);
    }

    [Theory]
    [InlineData(typeof(KanmanggaySpaceHolders), "/operations/kanmanggay/space-holders")]
    [InlineData(typeof(KanmanggayManageSpaceHolders), "/operations/kanmanggay/manage-space-holders")]
    [InlineData(typeof(VegetableFruitSpaceHolders), "/operations/vegetable-fruit/space-holders")]
    [InlineData(typeof(VegetableFruitManageSpaceHolders), "/operations/vegetable-fruit/manage-space-holders")]
    public void DedicatedRoutesKeepOfficeAuthorization(Type page, string route)
    {
        Assert.Equal(route, Assert.Single(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(page.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }
    [Fact]
    public void OverviewNavigatesToDistinctResponsibilitiesAndKeepsOfficeActions()
    {
        Serve(Account());
        var cut = RenderComponent<Kanmanggay>();
        Assert.Equal("/operations/kanmanggay/space-holders", cut.FindAll("a").Single(a => a.TextContent == "Space Holder List").GetAttribute("href"));
        Assert.Equal("/operations/kanmanggay/manage-space-holders", cut.FindAll("a").Single(a => a.TextContent == "Manage Space Holders").GetAttribute("href"));
        Assert.Empty(cut.FindAll("a[href='/operations/kanmanggay/import']"));
        Assert.NotNull(cut.Find("a[href='/operations/kanmanggay/report']"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent == "+ Add New");
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent is "Periods" or "Change amount" or "Close account");
        Assert.Equal("Space Rentals", cut.Find(".rental-summary-title").TextContent);
        Assert.Empty(cut.FindAll("tbody a"));
        Assert.Contains(cut.FindAll("thead th"), th => th.TextContent == "Actions");
        Assert.Equal(new[] { "₱", "↺", "→" }, cut.FindAll("tbody .action-btn").Select(x => x.TextContent));
        Assert.DoesNotContain(cut.FindAll("button, a"), b => b.TextContent.Trim() == "Open account");
        Assert.Contains("Start date", cut.Find("thead").TextContent);
        SaveVisualArtifact("kanmanggay-overview", cut.Markup);
    }

    [Fact]
    public void OverviewFiltersRowsWithoutChangingServerTotals()
    {
        Serve(Account("Unpaid holder"), Account("Paid holder", collected: 900m), Account("Closed holder", closed: true));
        var cut = RenderComponent<Kanmanggay>();
        var hero = cut.FindComponent<FacilityHero>().Markup;
        cut.FindAll(".filter-tab").Single(b => b.TextContent == "Paid").Click();
        Assert.Contains("Paid holder", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("Unpaid holder", cut.Find("tbody").TextContent);
        cut.FindAll(".filter-tab").Single(b => b.TextContent == "Closed").Click();
        Assert.Contains("Closed holder", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("Paid holder", cut.Find("tbody").TextContent);
        cut.Find(".search-input").Input("absent");
        Assert.Contains("No spaces found", cut.Find(".empty-state").TextContent);
        Assert.Equal(hero, cut.FindComponent<FacilityHero>().Markup);
    }

    [Fact]
    public void RosterIsTheTenColumnOfficialList_AndNeverInventsContractFactsForSpaceOnly()
    {
        Serve(Account(), Account("Signed holder", true) with { Arrangement = OccupancyArrangement.SignedContract, ContractReference = "LEASE-17", SubjectLabel = "008", ContractName = "Signed Name",
            ContractEffectiveDate = new(2026, 2, 3), ContractYears = 3, AreaSqm = 12m, ContractMonthlyRate = 800m });
        var cut = RenderComponent<KanmanggaySpaceHolders>();
        Assert.Equal("Kanmanggay Space Holder List", cut.Find(".lsh-hero-title").TextContent.Trim());
        Assert.Contains("KANMANGGAY SPACE HOLDER LIST", cut.Find(".lsh-doc-title").TextContent.ToUpperInvariant());
        Assert.Contains("Republic of the Philippines", cut.Markup);
        Assert.Equal("/operations/kanmanggay", cut.Find(".page-toolbar a[href='/operations/kanmanggay']").GetAttribute("href"));
        Assert.Equal(10, cut.FindAll(".lsh-table thead th").Count);
        Assert.Contains("Actual holder", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("Compatibility name", cut.Find("tbody").TextContent);
        Assert.Contains("No contract (space only)", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("Must not print", cut.Markup);
        var open = Assert.Single(cut.FindAll("tbody tr"));
        Assert.Equal(10, open.Children.Length);
        Assert.Equal(["₱900.00", "₱10,800.00"], open.Children.Skip(8).Select(x => x.TextContent.Trim()).ToArray());   // whole year = 12 x the approved monthly amount
        Assert.Equal("—", open.Children[4].TextContent.Trim());                                                         // no effectivity for a space-only occupancy
        Assert.Contains("projection", cut.Find(".lsh-footnote").TextContent);
        cut.FindAll(".lsh-seg-item").Single(b => b.TextContent.Trim() == "Closed").Click();
        var closed = Assert.Single(cut.FindAll("tbody tr")).Children.Select(x => x.TextContent.Trim()).ToArray();
        Assert.Contains("Signed Name", closed); Assert.Contains("2/3/2026", closed); Assert.Contains("12 sq.m.", closed); Assert.Contains("₱800.00", closed);
        cut.Find(".search-box input").Input("absent");
        Assert.Contains("No records match", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void FiestaArawRosterIsTheSixColumnLotList()
    {
        var lot = Account("Lot holder") with { Kind = ObligationKind.FiestaArawLotRental, Event = LotRentalEvent.Fiesta };
        _api.Setup(x => x.GetAccountsAsync(ObligationKind.FiestaArawLotRental)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([lot]));
        var cut = RenderComponent<FiestaArawSpaceHolders>();
        Assert.Equal(["#", "Name of Lessee", "Lot No.", "Event", "Approved Amount", "Contract"],
            cut.FindAll(".lsh-table thead th").Select(t => string.Join(' ', t.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray());
        Assert.Equal(6, Assert.Single(cut.FindAll("tbody tr")).Children.Length);
    }

    [Fact]
    public void CsvFollowsTheFilteredRosterAndPrintAndPdfUseTheLandscapeSheet()
    {
        Serve(Account("=FORMULA"), Account("Closed holder", true));
        var cut = RenderComponent<KanmanggaySpaceHolders>();
        var csv = Uri.UnescapeDataString(cut.Find("a[download$='.csv']").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains("'=FORMULA", csv);
        Assert.DoesNotContain("Closed holder", csv);
        Assert.DoesNotContain("Must not print", csv);
        Assert.StartsWith("#,Name of Lessee (Actual Occupants),Name of Lessee per Signed Contract,Space No.", csv);
        Click(cut, "Print"); Click(cut, "Save as PDF");
        Assert.Equal(2, JSInterop.Invocations["stalltrackPrint.landscape"].Count);
    }
    [Fact]
    public void FailedRosterHasRetryAndCannotIssueACsv()
    {
        _api.Setup(x => x.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Failure("Offline"));
        var cut = RenderComponent<KanmanggaySpaceHolders>();
        Assert.Contains("Offline", cut.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("No space holders registered", cut.Markup);
        Serve(Account()); Click(cut, "Try again");
        Assert.Contains("Actual holder", cut.Find("tbody").TextContent);
    }
    [Fact]
    public void LifecycleAndPaymentPositionAreIndependentAndTotalsKeepTheirFullScope()
    {
        Serve(Account("Active unpaid"), Account("Closed paid", true, 900m));
        var cut = RenderComponent<KanmanggayManageSpaceHolders>();
        Assert.Contains("Active unpaid", cut.Find("tbody").TextContent);
        SaveVisualArtifact("kanmanggay-management", cut.Markup);
        Click(cut, "Closed");
        cut.Find("[aria-label='Account lifecycle']").Change("Closed");
        cut.FindAll(".filter-tab").Single(b => b.TextContent == "Paid").Click();
        Assert.Contains("Closed paid", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("Active unpaid", cut.Find("tbody").TextContent);
        Assert.Contains("₱1,800.00", cut.FindComponent<RentalRegistryHero>().Markup);
        Assert.DoesNotContain("All accounts · to date", cut.FindComponent<RentalRegistryHero>().Markup);
        Assert.Contains("1 spaces", cut.Find(".vs-count-label").TextContent);
        Assert.DoesNotContain("Space Account Management", cut.Markup);
        Assert.DoesNotContain("Financial totals:", cut.Markup);
        Assert.NotNull(cut.Find(".obw-advanced-filters [aria-label='Account lifecycle']"));
        cut.FindAll(".filter-tab").Single(b => b.TextContent == "Unpaid").Click();
        Assert.DoesNotContain("Closed paid", cut.Find("tbody").TextContent);
        Assert.Contains("₱1,800.00", cut.FindComponent<RentalRegistryHero>().Markup);
    }
    [Fact]
    public void ClosureRequiresConfirmationAndPreservesTheExistingCommand()
    {
        var account = Account(); Serve(account);
        _api.Setup(x => x.CloseAccountAsync(account.Id, It.IsAny<CloseObligationAccountRequest>())).ReturnsAsync(Result<bool>.Success(true));
        var cut = RenderComponent<KanmanggayManageSpaceHolders>();
        cut.Find(".registry-table tbody tr").Click(); Click(cut, "Close account");
        _api.Verify(x => x.CloseAccountAsync(It.IsAny<Guid>(), It.IsAny<CloseObligationAccountRequest>()), Times.Never);
        Assert.Contains("Previously assessed periods and collections remain readable", cut.Find("[role='dialog']").TextContent);
        cut.Find("form[aria-label='Confirm account closure']").Submit();
        cut.WaitForAssertion(() => _api.Verify(x => x.CloseAccountAsync(account.Id, It.IsAny<CloseObligationAccountRequest>()), Times.Once));
    }
    [Fact]
    public void VegetableFruitRosterAndManagementReadOnlyTheirOwnAccountKind()
    {
        var account = Account() with { Kind = ObligationKind.VegetableFruitSpaceRental };
        _api.Setup(x => x.GetAccountsAsync(account.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([account, Account("Wrong source")]));
        _api.Setup(x => x.GetWorkspaceAsync(account.Kind)).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([account], 900m, 0m, 900m)));
        var roster = RenderComponent<VegetableFruitSpaceHolders>();
        Assert.Contains("Actual holder", roster.Find("tbody").TextContent);
        Assert.DoesNotContain("Wrong source", roster.Markup);
        var management = RenderComponent<VegetableFruitManageSpaceHolders>();
        Assert.Contains("Actual holder", management.Find("tbody").TextContent);
        SaveVisualArtifact("vegetable-fruit-roster", roster.Markup);
        SaveVisualArtifact("vegetable-fruit-management", management.Markup);
        _api.Verify(x => x.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental), Times.Never);
        _api.Verify(x => x.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental), Times.Never);
    }

    [Fact]
    public void ManagementKeepsEffectiveDatedRateCommandAndReportsPeriodFailures()
    {
        var account = Account(); Serve(account);
        _api.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Failure("Periods offline"));
        SetObligationRateRequest? sent = null;
        _api.Setup(x => x.SetRateAsync(account.Id, It.IsAny<SetObligationRateRequest>()))
            .Callback<Guid, SetObligationRateRequest>((_, request) => sent = request).ReturnsAsync(Result<bool>.Success(true));
        var cut = RenderComponent<KanmanggayManageSpaceHolders>();
        cut.Find(".registry-table tbody tr").Click();
        Assert.Contains("Periods offline", cut.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("No period is billable", cut.Markup);
        Assert.NotNull(cut.Find("dl.obw-account-facts"));
        Assert.DoesNotContain("Must not print", cut.Find("dl").TextContent);
        SaveVisualArtifact("kanmanggay-management-details", cut.Markup);
        Click(cut, "Change amount");
        cut.Find("#obw-rate-month").Change("2026-11"); cut.Find("#obw-rate-amount").Change("1000");
        cut.Find("form[aria-label='Change approved amount']").Submit();
        cut.WaitForAssertion(() => Assert.Equal(new SetObligationRateRequest(new(2026, 11, 1), 1000m), sent));
        _api.Verify(x => x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()), Times.Never);
    }

    [Fact]
    public void ManyRowsUseASingleOfficialDocumentWithTheFixedTenColumns()
    {
        Serve(Enumerable.Range(1, 90).Select(i => Account($"Test holder {i:000}") with {
            SubjectLabel = i.ToString("000"), Arrangement = i % 2 == 0 ? OccupancyArrangement.SignedContract : OccupancyArrangement.SpaceOnly,
            ContractReference = i % 2 == 0 ? $"TEST-LEASE-{i:000}" : null }).ToArray());
        var cut = RenderComponent<KanmanggaySpaceHolders>();
        Assert.Equal(90, cut.FindAll(".lsh-table tbody tr").Count);
        Assert.Single(cut.FindAll(".lsh-doc-panel"));
        Assert.Equal(10, cut.FindAll(".lsh-table thead th").Count);
        SaveVisualArtifact("kanmanggay-roster-many", cut.Markup);
    }

    internal static void SaveVisualArtifact(string name, string markup)
    {
        // Optional local visual harness: exports real rendered components, never product sample records or database writes.
        var output = Environment.GetEnvironmentVariable("STALLTRACK_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EEMOCantilanSDS.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        Directory.CreateDirectory(output);
        var client = Path.Combine(root!.FullName, "EEMOCantilanSDS.Client");
        string FileUrl(string path) => new Uri(path).AbsoluteUri;
        var styles = new[] { Path.Combine(client, "wwwroot", "css", "site.min.css"), Path.Combine(client, "wwwroot", "app.css"), Path.Combine(client, "wwwroot", "css", "payor.css"), Path.Combine(client, "obj", "Release", "net10.0", "scopedcss", "bundle", "EEMOCantilanSDS.Client.styles.css"), Path.Combine(client, "wwwroot", "css", "print.css") };
        var links = string.Join("", styles.Select(path => $"<link rel='stylesheet' href='{FileUrl(path)}'>"));
        // Root-relative static images must resolve to the same checked-out assets in file:// fixtures.
        markup = System.Text.RegularExpressions.Regex.Replace(markup, "src=\"/([^\"]+)\"", m => $"src=\"{FileUrl(Path.Combine(client, "wwwroot", m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)))}\"");
        File.WriteAllText(Path.Combine(output, name + ".html"), $"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><base href='{FileUrl(Path.Combine(client, "wwwroot"))}/'>{links}<script src='{FileUrl(Path.Combine(client, "wwwroot", "js", "print.js"))}'></script></head><body><div class='admin-layout'><main class='admin-main'>{markup}</main></div></body></html>");
    }
}
