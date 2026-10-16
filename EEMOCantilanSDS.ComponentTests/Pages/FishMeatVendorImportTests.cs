using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Fish / Meat vendor import: registry facts only, checked by the server before anything is saved; a possible duplicate is the Head's
/// explicit choice and is never merged by name; money columns are never read.</summary>
public sealed class FishMeatVendorImportTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private static readonly int Year = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year;
    private readonly Mock<IOfficeSourcesApiClient> _office = new();
    private VendorRegistryImportRequest? _previewed, _saved;

    public FishMeatVendorImportTests()
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
        // Row 2 looks like an existing registration; everything else is new.
        _office.Setup(x => x.PreviewImportAsync(It.IsAny<VendorRegistryImportRequest>())).Callback<VendorRegistryImportRequest>(r => _previewed = r)
            .ReturnsAsync((VendorRegistryImportRequest r) => Result<VendorRegistryImportPreview>.Success(new(r.Rows.Select(row => row.RowNumber == 2 && !row.ConfirmSeparateRegistration
                ? new VendorRegistryImportPreviewRow(row.RowNumber, row.Registration, VendorRegistryImportState.PossibleDuplicateRequiresReview, [new("PossibleDuplicate", "text that is never read")], [Guid.NewGuid()])
                : new VendorRegistryImportPreviewRow(row.RowNumber, row.Registration, VendorRegistryImportState.New, [], [])).ToList())));
        _office.Setup(x => x.SaveImportAsync(It.IsAny<VendorRegistryImportRequest>())).Callback<VendorRegistryImportRequest>(r => _saved = r)
            .ReturnsAsync((VendorRegistryImportRequest r) => Result<VendorRegistryImportResult>.Success(new(r.Rows.Select(row =>
                new VendorRegistryImportSavedRow(row.RowNumber, new(Guid.NewGuid(), row.Registration.TaxYear, row.Registration.VendorType, row.Registration.RegistrationKind,
                    row.Registration.DisplayName, row.Registration.BusinessName, row.Registration.Address, row.Registration.Reference))).ToList())));
    }

    private IRenderedComponent<FishMeatVendorImport> UploadCsv(string csv)
    {
        var cut = RenderComponent<FishMeatVendorImport>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "vendors.csv"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".imp-grid, table")), Timeout);
        return cut;
    }

    private const string Csv = "Vendor name,Business name,Type,Registration,Address,Reference,Amount for September\r\n" +
                               "Lisa Ilogans,Ilogans Fish Stall,Fish,New,Purok 3,REG-1,1250.00\r\n" +
                               "Pantom Dant,,Meat,Renew,,,900\r\n";

    [Fact]
    public void TheUploadStepNamesCsvAndExcel_OffersAHeaderOnlyTemplate_AndSaysMoneyIsNotRead()
    {
        var cut = RenderComponent<FishMeatVendorImport>();

        Assert.Contains("CSV or Excel", cut.Markup);
        var template = cut.Find("a[download]").GetAttribute("href")!;
        Assert.Equal("Vendor name,Business name,Last name,First name,Middle name,Extname,Sex,Type,Registration,Address,Nature of business,Reference\r\n", Uri.UnescapeDataString(template.Replace("data:text/csv;charset=utf-8,", "")));
        Assert.Contains("never guessed", cut.Markup);
        Assert.Contains("this list registers vendors, it does not record collections", cut.Markup);
        Assert.Empty(cut.FindAll("select"));                                                       // tax year is a styled selector
        Assert.Equal("Tax year", cut.Find(".fh-dd-cap").TextContent.Trim());
    }

    [Fact]
    public void AFileIsReadByItsHeader_AnAmountColumnIsIgnored_AndTheServerPreviewsEveryRow()
    {
        var cut = UploadCsv(Csv);

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(_previewed);
            Assert.Equal(2, _previewed!.Rows.Count);
            var first = _previewed.Rows[0];
            Assert.Equal((1, Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Lisa Ilogans", "Ilogans Fish Stall", "Purok 3", "REG-1"),
                (first.RowNumber, first.Registration.TaxYear, first.Registration.VendorType, first.Registration.RegistrationKind, first.Registration.DisplayName,
                 first.Registration.BusinessName, first.Registration.Address, first.Registration.Reference));
            Assert.Equal((2, FishMeatVendorType.Meat, VendorRegistrationKind.Renew, "Pantom Dant", (string?)null), (_previewed.Rows[1].RowNumber, _previewed.Rows[1].Registration.VendorType,
                _previewed.Rows[1].Registration.RegistrationKind, _previewed.Rows[1].Registration.DisplayName, _previewed.Rows[1].Registration.BusinessName));
            Assert.NotEqual(first.Registration.ClientOperationId, _previewed.Rows[1].Registration.ClientOperationId);        // one stable operation per row
            Assert.DoesNotContain("1250", System.Text.Json.JsonSerializer.Serialize(_previewed));                              // the handwritten money never travels
        }, Timeout);
    }

    [Fact]
    public void APossibleDuplicate_NeedsTheHeadsExplicitSeparateConfirmation_BeforeAnythingCanBeImported()
    {
        var cut = UploadCsv(Csv);

        cut.WaitForAssertion(() => Assert.Contains("Possible duplicate", cut.Markup), Timeout);
        Assert.True(cut.Find(".imp-foot-bar button.imp-btn-primary, .imp-footbar button.imp-btn-primary, button.imp-btn.imp-btn-primary").HasAttribute("disabled"));
        Assert.Contains("Looks like a vendor already registered for this year", cut.Markup);       // wording from the code, not the server text
        Assert.DoesNotContain("text that is never read", cut.Markup);

        cut.Find(".fmi-confirm input").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Possible duplicate", cut.Markup);
            Assert.False(cut.Find("button.imp-btn.imp-btn-primary").HasAttribute("disabled"));
            Assert.True(_previewed!.Rows.Single(r => r.RowNumber == 2).ConfirmSeparateRegistration);
        }, Timeout);
    }

    [Fact]
    public void AConfirmationStaysVisible_AndCanBeTakenBack()
    {
        var cut = UploadCsv(Csv);
        cut.WaitForAssertion(() => Assert.Contains("Possible duplicate", cut.Markup), Timeout);
        cut.Find(".fmi-confirm input").Change(true);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Possible duplicate", cut.Markup), Timeout);

        Assert.True(cut.Find(".fmi-confirm input").HasAttribute("checked"));                       // still there once confirmed
        cut.Find(".fmi-confirm input").Change(false);

        cut.WaitForAssertion(() => Assert.Contains("Possible duplicate", cut.Markup), Timeout);
        Assert.True(cut.Find("button.imp-btn.imp-btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public void ImportSavesTheConfirmedRows_ShowsHowManyWereImported_AndOffersManage()
    {
        var cut = UploadCsv(Csv);
        cut.WaitForAssertion(() => Assert.Contains("Possible duplicate", cut.Markup), Timeout);
        cut.Find(".fmi-confirm input").Change(true);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.imp-btn.imp-btn-primary").HasAttribute("disabled")), Timeout);

        cut.Find("button.imp-btn.imp-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(_saved);
            Assert.Equal([false, true], _saved!.Rows.Select(r => r.ConfirmSeparateRegistration).ToArray());
            Assert.Contains("2 vendors imported", cut.Markup);
            Assert.Equal("/operations/fish-meat-vendor-fees/manage", cut.FindAll("a").Single(a => a.TextContent.Contains("Open Manage")).GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void OldHeaderlessSixColumnUploadRetainsItsOriginalMapping()
    {
        var cut = UploadCsv("Lisa Ilogans,Ilogans Fish Stall,Fish,New,Purok 3,REG-1\r\n");
        cut.WaitForAssertion(() => Assert.NotNull(_previewed), Timeout);
        var registration = Assert.Single(_previewed!.Rows).Registration;
        Assert.Equal(FishMeatVendorType.Fish, registration.VendorType);
        Assert.Equal(VendorRegistrationKind.New, registration.RegistrationKind);
        Assert.Equal("Purok 3", registration.Address);
        Assert.Equal("REG-1", registration.Reference);
        Assert.Null(registration.NatureOfBusiness);
    }

    [Fact]
    public void OfficePaperColumnsPreserveNameAndBusinessNatureButNeverImportHandwrittenMoney()
    {
        var csv = $"Business Name,Last Name,First Name,Middle Name,Extname,Sex,Type,Type of,Tax Year,Address,Nature of Business,Amount\r\n" +
                  $"LEMUEL FISH RETAILING,LORCHANO,LEMUEL,ALFANTA,,Male,Fish,Renew,{Year},Purok 5,Retail sale of fish and other seafood (fresh and dried),16621\r\n";
        var cut = UploadCsv(csv);
        cut.WaitForAssertion(() => Assert.NotNull(_previewed), Timeout);
        var entry = Assert.Single(_previewed!.Rows).Registration;
        Assert.Equal("LEMUEL ALFANTA LORCHANO", entry.DisplayName);
        Assert.Equal("LEMUEL FISH RETAILING", entry.BusinessName);
        Assert.Equal("LORCHANO", entry.LastName);
        Assert.Equal("LEMUEL", entry.FirstName);
        Assert.Equal("ALFANTA", entry.MiddleName);
        Assert.Equal("Male", entry.Sex);
        Assert.Equal("Retail sale of fish and other seafood (fresh and dried)", entry.NatureOfBusiness);
        Assert.Equal(FishMeatVendorType.Fish, entry.VendorType);
        Assert.DoesNotContain("16621", System.Text.Json.JsonSerializer.Serialize(_previewed));
        Assert.Contains("imp-hero-blue", cut.Markup);
    }

    [Fact]
    public void OfficeRegisterShowsSeparateVendorFeeAndWeightMeasureWithoutCountingWalkupTwice()
    {
        var year = Year;
        var month = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Month;
        var vendor = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year,
            FishMeatVendorType.Fish, VendorRegistrationKind.Renew, "LEMUEL ALFANTA LORCHANO",
            "LEMUEL FISH RETAILING", "Purok 5", null, "LORCHANO", "LEMUEL", "ALFANTA",
            null, "Male", "Retail sale of fish and other seafood (fresh and dried)");
        var row = new VendorRegistrationManagementRow(vendor, VendorRegistrationStatus.Active, "Head",
            DateTime.UtcNow, null, null, null, null, null, 250m, 45m);
        _office.Setup(x => x.ManageRegistrationsAsync(year, month, null, year))
            .ReturnsAsync(Result<VendorRegistryManagement>.Success(new VendorRegistryManagement(year, year, month, [row], 20m)));

        var cut = RenderComponent<FishMeatVendorOfficeRegister>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("LEMUEL FISH RETAILING", cut.Find(".fmo-scroll").TextContent);
            Assert.Contains("LORCHANO", cut.Find(".fmo-scroll").TextContent);
            Assert.Contains("Retail sale of fish and other seafood", cut.Find(".fmo-scroll").TextContent);
            Assert.Contains("₱250.00", cut.Find(".fmo-scroll").TextContent);
            Assert.Contains("₱45.00", cut.Find(".fmo-scroll").TextContent);
            Assert.Contains("₱20.00", cut.Find(".fmo-note").TextContent);
            Assert.Contains("₱270.00", cut.Find(".facility-hero").TextContent);
            Assert.NotNull(cut.Find("#fmo-month"));
            var selectors = cut.FindComponents<EEMOCantilanSDS.Client.Components.Shared.FhSelect>();
            Assert.Equal(2, selectors.Count);
            Assert.Equal("Registration tax year", selectors[0].Instance.AriaLabel);
            Assert.Equal("Vendor type", selectors[1].Instance.AriaLabel);
            Assert.Contains("About this register", cut.Markup);
            Assert.Single(cut.FindAll(".fmo-note"));
            Assert.Single(cut.FindAll(".fmo-method summary"));
        }, Timeout);
        var typeSelector = cut.FindComponents<EEMOCantilanSDS.Client.Components.Shared.FhSelect>()[1];
        typeSelector.Find(".fh-dd-trigger").Click();
        typeSelector.FindAll(".fh-dd-item").Single(x => x.TextContent.Trim() == "Meat").Click();
        Assert.Contains("No registrations match this view.", cut.Markup);
        typeSelector.Find(".fh-dd-trigger").Click();
        typeSelector.FindAll(".fh-dd-item").Single(x => x.TextContent.Trim() == "Fish").Click();
        Assert.Contains("LEMUEL FISH RETAILING", cut.Find(".fmo-scroll").TextContent);

        _office.Setup(x => x.ManageRegistrationsAsync(year + 1, month, null, year))
            .ReturnsAsync(Result<VendorRegistryManagement>.Success(new VendorRegistryManagement(year + 1, year, month, [], 0m)));
        var yearSelector = cut.FindComponents<EEMOCantilanSDS.Client.Components.Shared.FhSelect>()[0];
        yearSelector.Find(".fh-dd-trigger").Click();
        yearSelector.FindAll(".fh-dd-item").Single(x => x.TextContent.Trim() == (year + 1).ToString()).Click();
        cut.WaitForAssertion(() => Assert.Contains("No registrations match this view.", cut.Markup), Timeout);

        _office.Verify(x => x.ManageRegistrationsAsync(year, month, null, year), Times.Once);
        _office.Verify(x => x.ManageRegistrationsAsync(year + 1, month, null, year), Times.Once);
    }

    [Fact]
    public void OfficePaperWithNoExplicitVendorTypeDoesNotInferFishFromNatureText()
    {
        var cut = UploadCsv($"Business Name,Last Name,First Name,Type of,Tax Year,Nature of Business\r\nFish shop,Lopez,Ana,Renew,{Year},Retail sale of fish and other seafood\r\n");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose Fish or Meat", cut.Markup);
            Assert.Contains("Invalid", cut.Markup);
        }, Timeout);
        _office.Verify(x => x.PreviewImportAsync(It.IsAny<VendorRegistryImportRequest>()), Times.Never);
    }

    [Fact]
    public void AnImportThatNeedsReview_IsReportedFromItsCode_AndKeepsTheList()
    {
        _office.Setup(x => x.SaveImportAsync(It.IsAny<VendorRegistryImportRequest>())).ReturnsAsync(Result<VendorRegistryImportResult>.Failure("ImportNeedsReview", ResultStatus.Conflict));
        var cut = UploadCsv("Vendor name,Type,Registration\r\nLisa Ilogans,Fish,New\r\n");
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.imp-btn.imp-btn-primary").HasAttribute("disabled")), Timeout);

        cut.Find("button.imp-btn.imp-btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("Some rows need review", cut.Markup), Timeout);
        Assert.Contains("Lisa Ilogans", cut.Markup);
    }

    [Fact]
    public void ARowWithoutAReadableTypeOrRegistration_IsInvalidHere_NeverGuessed()
    {
        var cut = UploadCsv("Vendor name,Type,Registration\r\nLisa Ilogans,fresh fish,\r\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Invalid", cut.Markup);
            Assert.Contains("Choose Fish or Meat", cut.Markup);
            Assert.True(cut.Find("button.imp-btn.imp-btn-primary").HasAttribute("disabled"));
        }, Timeout);
        _office.Verify(x => x.PreviewImportAsync(It.IsAny<VendorRegistryImportRequest>()), Times.Never);        // an unreadable row is not even sent
    }
}
