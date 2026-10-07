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
        Assert.Equal("Vendor name,Business name,Type,Registration,Address,Reference\r\n", Uri.UnescapeDataString(template.Replace("data:text/csv;charset=utf-8,", "")));
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
