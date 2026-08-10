using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
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

    /// <summary>
    /// The facility's own stalls, so the pick-list has something in it. Two occupied and one closed, because a closed
    /// stall's number must not be offered as a payor.
    /// </summary>
    private static StallHoldersListDto Holders() => new()
    {
        Sections =
        [
            new StallHoldersSectionDto
            {
                Rows =
                [
                    new StallHolderRowDto
                    {
                        StallNo = "1", ActualOccupant = "George Giovanna",
                        EffectivityDate = PhilippineTime.Today.AddYears(-1), DurationYears = 3
                    },
                    new StallHolderRowDto
                    {
                        StallNo = "2", ActualOccupant = "Ackerman Tril",
                        EffectivityDate = PhilippineTime.Today.AddYears(-1), DurationYears = 3
                    },
                    new StallHolderRowDto
                    {
                        StallNo = "9", ActualOccupant = "Vacated Vendor", IsClosed = true,
                        EffectivityDate = PhilippineTime.Today.AddYears(-1), DurationYears = 3
                    }
                ]
            }
        ]
    };

    private IRenderedComponent<ImportPaymentHistory> Render(string facility)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _payments = new Mock<IPaymentsApiClient>();
        Services.AddSingleton(_payments.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());

        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallHoldersListAsync(It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>()))
              .ReturnsAsync(Result<StallHoldersListDto>.Success(Holders()));
        Services.AddSingleton(stalls.Object);
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

        Assert.Contains("Choose a file to upload", cut.Markup);
        Assert.Contains("Download CSV template", cut.Markup);
    }

    [Fact]
    public void TheMarketIsRefused_WithTheReasonStated()
    {
        var cut = Render("npm");

        // Refused, and the office is told WHY rather than finding the option simply absent - which would read as
        // an oversight and invite someone to force it through another route.
        Assert.Contains("per market day", cut.Markup);
        Assert.DoesNotContain("Choose a file to upload", cut.Markup);
    }

    [Fact]
    public void ThePageWalksTheSameThreeStepsAsTheStallholderImport()
    {
        var cut = Render("tcc");

        // The office meets both screens in the same sitting; a second layout for the same task is a second thing
        // to learn.
        Assert.Contains("Upload", cut.Markup);
        Assert.Contains("Review &amp; edit", cut.Markup);
        Assert.Contains("Save", cut.Markup);
        Assert.Equal(3, cut.FindAll(".iph-step").Count);
    }

    [Fact]
    public void TheNativeFileInputIsNotLeftShowingThroughTheDropzone()
    {
        var cut = Render("tcc");

        // InputFile renders its input inside a child component, so a scoped class on the element does not reach it
        // and the browser's own "Choose File / No file chosen" showed through the middle of the dropzone. The rule
        // that hides it needs ::deep, which is easy to lose in a later edit - hence this.
        var input = cut.Find(".iph-drop input[type=file]");
        Assert.NotNull(input);
        Assert.DoesNotContain("No file chosen", cut.Markup);
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

        cut.Find("button.iph-use-sample").Click();

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

        cut.Find("button.iph-use-sample").Click();

        // So the office meets the part-paid case in the sample rather than for the first time in its own history,
        // where a remaining balance could be mistaken for a failed import. Read from the editable cell's value.
        var amounts = cut.FindAll("input.iph-input-num").Select(i => i.GetAttribute("value")).ToList();
        Assert.Contains(amounts, a => a is not null && a.StartsWith("1200"));
        Assert.Contains(amounts, a => a is not null && a.StartsWith("2400"));
    }

    [Fact]
    public void EnteringManuallyStartsAnEmptyReviewTable()
    {
        var cut = Render("tcc");

        // For an office with no file at all. It reaches the SAME review table, starting empty, so rows entered by
        // hand are checked and saved exactly as uploaded ones are - one path to verify rather than two.
        // Named, not indexed. The commit that removed FindAll(...)[1] from another test yesterday existed because
        // that pattern failed a deployment; reaching for it again here would have been the same mistake twice.
        cut.Find("button.iph-enter-manually").Click();

        Assert.Contains("rows ready to review", cut.Markup);
        Assert.Single(cut.FindAll("table.iph-table-edit tbody tr"));
        Assert.Contains("Entered by hand", cut.Markup);

        // Empty, not pre-filled: a blank ledger row must not arrive carrying someone else's figures.
        var stallCells = cut.FindAll("table.iph-table-edit tbody tr input");
        Assert.All(stallCells.Take(3), input => Assert.True(string.IsNullOrEmpty(input.GetAttribute("value"))));
    }

    [Fact]
    public void ThreeWaysToStartAreOffered()
    {
        var cut = Render("tcc");

        Assert.Contains("Download CSV template", cut.Markup);
        Assert.Contains("Use sample data instead", cut.Markup);
        Assert.Contains("Enter manually", cut.Markup);
    }

    [Fact]
    public void LeavingAnImportReturnsToTheFacility()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        // Not to whatever page came before. Someone who abandons an import wants the facility they were importing
        // into, and the canonical route is the one the sidebar and the address bar use.
        var cancel = cut.FindAll("a.iph-btn").First(a => a.TextContent.Contains("Cancel"));
        Assert.Equal("/facility/tcc", cancel.GetAttribute("href"));
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

    [Fact]
    public void TheOfficeCanPickAPayorInsteadOfRememberingAStallNumber()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        // A mistyped stall number is the one error the import cannot detect for itself - 3 is as valid a stall as 8 -
        // so the facility's own stalls are offered by name.
        var options = cut.FindAll("datalist#iph-payors option");
        Assert.Equal(2, options.Count);
        Assert.Equal("1", options[0].GetAttribute("value"));
        Assert.Contains("George Giovanna", options[0].GetAttribute("label"));

        // A closed stall is not a payor and must not be offered.
        Assert.DoesNotContain("Vacated Vendor", cut.Markup);

        // Still a plain text box: an office working from an older sheet may need a number nobody occupies today.
        Assert.Equal("iph-payors", cut.Find("input.iph-input[list]").GetAttribute("list"));
    }

    [Fact]
    public void ChoosingAStallNamesTheVendorItBelongsTo()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        cut.Find("input.iph-input[list]").Input("2");

        // Named where the occupant goes, so a wrong number shows itself before the row is saved rather than after the
        // money has landed on somebody else's month.
        Assert.Contains("Ackerman Tril", cut.Markup);
    }

    [Fact]
    public void AMonthThatHasNotStartedYetIsMarkedAndRefused()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        var next = PhilippineTime.Today.AddMonths(2);
        cut.Find("input.iph-period").Input($"{next.Year:0000}-{next.Month:00}");

        // Marked while typing...
        Assert.Contains("iph-input-bad", cut.Markup);

        // ...and refused on Save, because a history is money already received. Reaching the server would have settled
        // rent nobody has been billed for.
        cut.Find("button.iph-btn-primary").Click();
        Assert.Contains("not started yet", cut.Markup);
        _payments.Verify(
            p => p.ImportPaymentHistoryAsync(It.IsAny<BulkImportPaymentHistoryCommand>()),
            Times.Never);
    }
}
