using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Manage Fish / Meat Vendors: any tax year and month, status, the detail drawer, and the Head's Renew and Close. There is no delete.</summary>
public sealed class FishMeatVendorManageTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficeSourcesApiClient> _office = new();
    private static readonly int Year = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year;
    private static readonly int Month = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Month;
    private readonly VendorRegistrationManagementRow _lisa, _pantom, _closed;

    public FishMeatVendorManageTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_office.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _lisa = FishMeatVendorRegistryTests.Row("Lisa Ilogans", FishMeatVendorType.Fish, VendorRegistrationKind.New, "Ilogans Fish Stall", "Purok 3", "REG-1", fee: 120m, weighing: 66m, prior: Guid.NewGuid());
        _pantom = FishMeatVendorRegistryTests.Row("Pantom Dant", FishMeatVendorType.Meat, VendorRegistrationKind.Renew);
        _closed = FishMeatVendorRegistryTests.Row("Old Vendor", FishMeatVendorType.Fish, VendorRegistrationKind.New, status: VendorRegistrationStatus.Closed);
        _office.Setup(x => x.ManageRegistrationsAsync(Year, Month, null, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(new(Year, Year, Month, [_lisa, _pantom, _closed], 75m)));
    }

    private static void Pick(IRenderedComponent<FishMeatVendorManage> cut, string caption, string option)
    {
        cut.FindAll(".fh-dd-trigger").Single(t => t.TextContent.Contains(caption)).Click();
        cut.FindAll(".fh-dd-item").Single(i => i.TextContent.Trim() == option).Click();
    }

    private static void Row(IRenderedComponent<FishMeatVendorManage> cut, string vendor, string button) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(vendor)).QuerySelectorAll("button").First(b => b.TextContent.Trim() == button).Click();

    [Fact]
    public void TaxYearMonthAndStatusLiveHere_AsStyledSelectors_NextToTheSearchAndTypeFilter()
    {
        var cut = RenderComponent<FishMeatVendorManage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Manage Fish / Meat Vendors", cut.Find("h1").TextContent.Trim());
            Assert.Empty(cut.FindAll("select"));
            var captions = cut.FindAll(".fh-dd-trigger .fh-dd-cap").Select(c => c.TextContent.Trim()).ToArray();
            Assert.Equal(["Tax year", "Month", "Status"], captions);
            Assert.Equal(["All", "Fish", "Meat"], cut.FindAll(".filter-tab").Select(t => t.TextContent.Trim()).ToArray());
            Assert.NotNull(cut.Find("input.search-input"));
        }, Timeout);
    }

    [Fact]
    public void TheTableShowsTheOfficeFactsOnly_WithStatusAndTheMonthsCollection_AndOffersNoDelete()
    {
        var cut = RenderComponent<FishMeatVendorManage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Vendor", "Type", "Registration", "Reference", "Status", "Collected", "Actions"], cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ToArray());
            var rows = cut.FindAll("tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Lisa Ilogans") && r.Contains("Ilogans Fish Stall · Purok 3") && r.Contains("Active") && r.Contains("₱186.00"));
            Assert.Contains(rows, r => r.Contains("Old Vendor") && r.Contains("Closed"));
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Delete", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Walk-up Vendor Fee collected", cut.Find(".fmm-foot").TextContent);
            Assert.Contains("₱75.00", cut.Find(".fmm-foot").TextContent);
        }, Timeout);
        var active = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Lisa"));
        var closed = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Old Vendor"));
        Assert.Equal(["Details", "Renew", "Close"], active.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.Equal(["Details", "Renew"], closed.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void ChangingTheTaxYear_MonthOrStatus_AsksTheServerForThatSlice()
    {
        _office.Setup(x => x.ManageRegistrationsAsync(Year - 1, Month, null, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(
            new(Year - 1, Year - 1, Month, [FishMeatVendorRegistryTests.Row("Earlier Vendor", FishMeatVendorType.Meat, VendorRegistrationKind.New, year: Year - 1)], 0m)));
        _office.Setup(x => x.ManageRegistrationsAsync(Year - 1, 3, VendorRegistrationStatus.Closed, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(new(Year - 1, Year - 1, 3, [], 0m)));
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);

        Pick(cut, "Tax year", (Year - 1).ToString());
        cut.WaitForAssertion(() => Assert.Contains("Earlier Vendor", cut.Markup), Timeout);
        Pick(cut, "Month", "March");
        Pick(cut, "Status", "Closed");

        cut.WaitForAssertion(() => _office.Verify(x => x.ManageRegistrationsAsync(Year - 1, 3, VendorRegistrationStatus.Closed, null), Times.Once), Timeout);
    }

    [Fact]
    public void TheDetailsDrawerShowsTheRegistrationAndTheBreakdown_WithAuditQuiet()
    {
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);

        Row(cut, "Lisa", "Details");

        var drawer = cut.Find(".eemo-drawer.show");
        var facts = drawer.QuerySelector("dl[aria-label='Registration details']")!.TextContent;
        foreach (var fact in new[] { "Lisa Ilogans", "Ilogans Fish Stall", "Purok 3", "Fish", "REG-1", "Active", "follows an earlier registration" }) Assert.Contains(fact, facts);
        var money = drawer.QuerySelector("dl[aria-label='Monthly collections']")!.TextContent;
        Assert.Contains("Vendor Fee", money); Assert.Contains("₱120.00", money);
        Assert.Contains("Weight & Measure", money); Assert.Contains("₱66.00", money);
        Assert.Contains("₱186.00", money);
        Assert.Contains("Registered by head", drawer.QuerySelector(".fmm-audit")!.TextContent);
    }

    [Fact]
    public void RenewPrefillsTheRegistration_FixesTheType_AndSendsTheHeadsExplicitChoicesForALaterYear()
    {
        Guid? source = null; RenewVendorRegistrationRequest? sent = null;
        var older = FishMeatVendorRegistryTests.Row("Old Fish", FishMeatVendorType.Fish, VendorRegistrationKind.New, "Old Fish Stall", "Purok 3", "REG-1", year: Year - 1);
        _office.Setup(x => x.ManageRegistrationsAsync(Year, Month, null, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(new(Year, Year, Month, [older], 0m)));
        _office.Setup(x => x.RenewRegistrationAsync(It.IsAny<Guid>(), It.IsAny<RenewVendorRegistrationRequest>()))
            .Callback<Guid, RenewVendorRegistrationRequest>((id, r) => { source = id; sent = r; })
            .ReturnsAsync((Guid id, RenewVendorRegistrationRequest r) => Result<VendorRegistrationMutationResult>.Success(new(
                new(Guid.NewGuid(), r.TaxYear, FishMeatVendorType.Fish, VendorRegistrationKind.Renew, r.DisplayName!, r.BusinessName, r.Address, r.Reference), VendorRegistrationStatus.Active, id, null, null, null, null, false)));
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Old Fish", cut.Markup), Timeout);

        Row(cut, "Old Fish", "Renew");

        var form = cut.Find("form[aria-label='Renew registration']");
        Assert.Equal("Old Fish", cut.Find("#fmm-renew-form-name").GetAttribute("value"));
        Assert.Equal("Old Fish Stall", cut.Find("#fmm-renew-form-business").GetAttribute("value"));
        Assert.Contains("Fish", form.QuerySelector(".avm-input-locked")!.TextContent);
        Assert.Empty(form.QuerySelectorAll("[role='radio']"));
        cut.Find("#fmm-renew-form-address").Input("Purok 4");
        form.Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(older.Registration.Id, source);
            Assert.NotEqual(Guid.Empty, sent!.ClientOperationId);
            Assert.Equal((Year, "Old Fish", "Old Fish Stall", "Purok 4", "REG-1"), (sent.TaxYear, sent.DisplayName, sent.BusinessName, sent.Address, sent.Reference));
            Assert.Contains($"renewed for {Year}", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void ARenewalThatAlreadyExists_IsExplainedFromItsCode_NotItsText()
    {
        _office.Setup(x => x.RenewRegistrationAsync(It.IsAny<Guid>(), It.IsAny<RenewVendorRegistrationRequest>()))
            .ReturnsAsync(Result<VendorRegistrationMutationResult>.Failure("RenewalAlreadyExists", ResultStatus.Conflict));
        var older = FishMeatVendorRegistryTests.Row("Old Fish", FishMeatVendorType.Fish, VendorRegistrationKind.New, year: Year - 1);
        _office.Setup(x => x.ManageRegistrationsAsync(Year, Month, null, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(new(Year, Year, Month, [older], 0m)));
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Old Fish", cut.Markup), Timeout);
        Row(cut, "Old Fish", "Renew");

        cut.Find("form[aria-label='Renew registration']").Submit();

        cut.WaitForAssertion(() => Assert.Contains("already exists", cut.Find("form[aria-label='Renew registration'] [role=alert]").TextContent), Timeout);
    }

    [Fact]
    public void CloseExplainsThatHistoryStays_SendsTheOptionalNote_AndRefreshes()
    {
        Guid? target = null; CloseVendorRegistrationRequest? sent = null;
        _office.Setup(x => x.CloseRegistrationAsync(It.IsAny<Guid>(), It.IsAny<CloseVendorRegistrationRequest>()))
            .Callback<Guid, CloseVendorRegistrationRequest>((id, r) => { target = id; sent = r; })
            .ReturnsAsync((Guid id, CloseVendorRegistrationRequest r) => Result<VendorRegistrationMutationResult>.Success(new(_lisa.Registration, VendorRegistrationStatus.Closed, null, new DateOnly(2026, 10, 7), DateTime.UtcNow, "head", r.Note, false)));
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);

        Row(cut, "Lisa", "Close");

        var text = cut.Find("form[aria-label='Close registration']").TextContent;
        Assert.Contains("Collections, SRCs and history stay", text);
        Assert.DoesNotContain("delete", text, StringComparison.OrdinalIgnoreCase);
        cut.Find("#fmm-close-note").Input("No longer trading");
        cut.Find("form[aria-label='Close registration']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(_lisa.Registration.Id, target);
            Assert.Equal("No longer trading", sent!.Note);
            Assert.Contains("closed for", cut.Markup);
        }, Timeout);
        _office.Verify(x => x.ManageRegistrationsAsync(Year, Month, null, null), Times.AtLeast(2));
    }

    [Fact]
    public void AnAlreadyClosedRegistration_IsReportedFromItsCode()
    {
        _office.Setup(x => x.CloseRegistrationAsync(It.IsAny<Guid>(), It.IsAny<CloseVendorRegistrationRequest>()))
            .ReturnsAsync(Result<VendorRegistrationMutationResult>.Failure("RegistrationClosed", ResultStatus.Conflict));
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);
        Row(cut, "Lisa", "Close");

        cut.Find("form[aria-label='Close registration']").Submit();

        cut.WaitForAssertion(() => Assert.Contains("already closed", cut.Find("form[aria-label='Close registration'] [role=alert]").TextContent), Timeout);
    }

    [Fact]
    public void AnAdminCanReadAndOpenDetails_ButHasNoRenewOrClose()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut = RenderComponent<FishMeatVendorManage>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);

        var buttons = cut.FindAll("tbody tr button").Select(b => b.TextContent.Trim()).Distinct().ToArray();
        Assert.Equal(["Details"], buttons);
    }
}
