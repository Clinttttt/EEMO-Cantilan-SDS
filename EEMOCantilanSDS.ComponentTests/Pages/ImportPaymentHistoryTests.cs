using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using ImportPaymentHistory = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.ImportPaymentHistory;

/// <summary>
/// The screen an office uses to record the payments it already collected.
///
/// <para>
/// The facility check is asserted here because it is the one thing this page must refuse. The market bills per
/// market day, so a month there is many collections rather than one payment; recording it through a one-row-per-
/// month sheet would settle days nobody collected. The server refuses it too - this is the office being told
/// before it prepares a file rather than after it uploads one.
/// </para>
/// </summary>
public class ImportPaymentHistoryTests : TestContext
{
    private Mock<IPaymentsApiClient> _payments = new();

    private IRenderedComponent<ImportPaymentHistory> Render(string facility)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _payments = new Mock<IPaymentsApiClient>();
        Services.AddSingleton(_payments.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<ImportPaymentHistory>(p => p.Add(c => c.Facility, facility));
    }

    [Theory]
    [InlineData("tcc")]
    [InlineData("ncc")]
    [InlineData("bbq")]
    [InlineData("ice")]
    public void AMonthlyBilledFacilityCanImportItsHistory(string facility)
    {
        var cut = Render(facility);

        Assert.Contains("Choose a CSV file", cut.Markup);
        Assert.DoesNotContain("Not for", cut.Markup);
    }

    [Fact]
    public void TheMarketIsRefused_WithTheReasonStated()
    {
        var cut = Render("npm");

        // Refused, and the office is told WHY rather than finding the option simply absent - which would read as
        // an oversight and invite someone to force it through another route.
        Assert.Contains("per market day", cut.Markup);
        Assert.DoesNotContain("Choose a CSV file", cut.Markup);
    }

    [Fact]
    public void TheTemplateCarriesTheColumnsInTheOrderTheSheetIsRead()
    {
        var cut = Render("tcc");

        // The parser reads by POSITION, so a template whose columns drifted from that order would silently record
        // an OR number as an amount. The header is asserted against the order the page documents.
        var link = cut.Find("a.iph-link");
        var href = Uri.UnescapeDataString(link.GetAttribute("href") ?? string.Empty);

        var header = href.Split('\n')[0];
        Assert.Contains("Stall / Space No.", header);
        Assert.Contains("Period (YYYY-MM)", header);
        Assert.Contains("Amount Paid", header);
        Assert.Contains("OR No.", header);

        Assert.True(
            header.IndexOf("Period", StringComparison.Ordinal) < header.IndexOf("Amount Paid", StringComparison.Ordinal),
            "the template must list Period before Amount Paid, because the sheet is read by position");
    }

    [Fact]
    public void TheOfficeIsToldThatAShortPaymentLeavesTheMonthOutstanding()
    {
        var cut = Render("tcc");

        // Said before the upload, not after: it is the difference between a clerk thinking the import failed and
        // understanding that the remaining balance is their own figure.
        Assert.Contains("stays outstanding", cut.Markup);
    }

    [Fact]
    public void TheOfficeIsToldThatAnOrNumberIsRequired()
    {
        var cut = Render("tcc");

        Assert.Contains("OR No.", cut.Markup);
        Assert.Contains("required", cut.Markup);
    }

    [Fact]
    public void TheSampleOfferSitsBesideTheTemplate()
    {
        var cut = Render("tcc");

        // The same pair of choices the stallholder import offers, so the two screens read alike.
        Assert.Contains("Download CSV template", cut.Markup);
        Assert.Contains("Use sample data instead", cut.Markup);
    }

    [Fact]
    public void TheSampleIsDatedFromTheCurrentMonth_SoItCannotRot()
    {
        var cut = Render("tcc");

        cut.Find("button.iph-link-btn").Click();

        // The stallholder sample was written with a fixed date and, three years on, produced rows that arrived
        // already expired. A payment sample dated in the past would be rejected month by month and read as the
        // feature being broken, so it is derived from today.
        var thisMonth = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today;
        Assert.Contains($"{thisMonth.AddMonths(-1):yyyy-MM}", cut.Markup);
        Assert.Contains($"{thisMonth.AddMonths(-3):yyyy-MM}", cut.Markup);
    }

    [Fact]
    public void TheSampleShowsAPartPaidMonth()
    {
        var cut = Render("tcc");

        cut.Find("button.iph-link-btn").Click();

        // So the office meets the part-paid case in the sample rather than for the first time in its own history,
        // where a remaining balance could be mistaken for a failed import.
        Assert.Contains("1,200", cut.Markup);
    }

    [Fact]
    public void NothingIsSentUntilThereAreRowsToSend()
    {
        Render("tcc");

        // The page must not call the API on load. A history import writes to the ledger; it happens when the office
        // asks for it and not before.
        _payments.Verify(
            p => p.ImportPaymentHistoryAsync(It.IsAny<BulkImportPaymentHistoryCommand>()),
            Times.Never);
    }
}
