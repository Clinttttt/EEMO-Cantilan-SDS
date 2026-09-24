using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using SummaryDocument = EEMOCantilanSDS.Client.Components.Pages.Reports.FinancialSummaryDocument;

/// <summary>
/// The Financial Summary document — the sheet the office files and signs.
///
/// <para>
/// It replaced a print of the Financial Reports screen, which inherited that screen's cards, broke wherever they fell,
/// and stated only the sections the office had not navigated away from. These tests hold the two things that matters
/// about the replacement: every part of the report is on the sheet whatever the screen was showing, and no figure on it
/// is a second, independently-summed version of a figure the report already states.
/// </para>
/// </summary>
public class FinancialSummaryDocumentTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    private static FinancialReportDto Report() => new(
        PeriodLabel: "September 2026",
        ScopeLabel: "All facilities",
        Frequency: "Monthly",
        FacilityCount: 8,
        Collected: 2_749m,
        CurrentPeriodUnpaid: 12_192m,
        Billed: 14_941m,
        CollectionRatePct: 18,
        PaidRecords: 39,
        ExpectedRecords: 242,
        CollectedPreviousPeriod: 8_877m,
        PreviousPeriodLabel: "August 2026",
        Delinquent: new List<AttentionAccountDto>
        {
            new("Rosa Magbanua", FacilityCode.TCC, "04", "TCC · Stall 04", 4_800m, 3),
            new("Merlita A. Abuso", FacilityCode.ICE, "7", "ICE · Stall 7", 33_300m, 37, TermLapsed: true),
            new("Jose Dalumpines", FacilityCode.NCC, "11-B", "NCC · Stall 11-B", 3_600m, 2)
        },
        Arrears: null,
        Aging: new List<ReceivableAgingBandDto>
        {
            new("1–2 months", 1, 3_600m),
            new("3–5 months", 1, 4_800m),
            new("6–11 months", 0, 0m),
            new("12+ months", 1, 33_300m)
        },
        LapsedWithBalanceCount: 1,
        LapsedWithBalanceOutstanding: 33_300m,
        Trend: new List<ReportTrendPointDto>
        {
            new("Aug 2026", 2026, 8, 8_877m, 4_000m, false),
            new("Sep 2026", 2026, 9, 2_749m, 12_192m, true)
        },
        YtdCollected: 11_626m,
        Facilities: new List<FinancialFacilityRowDto>
        {
            new(FacilityCode.NPM, "New Public Market", "Daily stall", false, 1_099m, 2_592m, 31, 230, 30, "Behind"),
            new(FacilityCode.TCC, "Tampak Commercial Center", "Monthly rental", false, 0m, 7_200m, 0, 3, 0, "Behind"),
            new(FacilityCode.BBQ, "Barbecue Stand", "Monthly rental", false, 0m, 0m, 0, 0, 0, "Nothing billed"),
            new(FacilityCode.SLH, "Slaughterhouse", "Per-head", true, 1_230m, null, 4, 0, 100, "Paid on service"),
            new(FacilityCode.TPM, "Tabo-an Public Market", "Weekly market", true, 420m, null, 4, 0, 100, "Paid on service")
        },
        RecentRecords: new List<FinancialRecordDto>(),
        ClosedWithBalanceCount: 2,
        ClosedWithBalanceOutstanding: 11_370m,
        AttentionSpanLabel: "since January 2026",
        DelinquentAccountsTotal: 7,
        DelinquentOutstandingTotal: 41_700m,
        Misc: new FinancialMiscDto(12m, 0m, 12m, 0, 1, true, new List<MonthEndUtilityRowDto>
        {
            new("1", "Kim Chui", 12m, 0m, 0m, 0m, null, FacilityCode.NPM, PaymentStatus.Unpaid, PaymentStatus.Unpaid)
        }));

    private IRenderedComponent<SummaryDocument> RenderDocument(FinancialReportDto? dto)
    {
        var api = new Mock<IReportsApiClient>();
        api.Setup(a => a.GetFinancialReportAsync(
                It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<FacilityCode?>(), It.IsAny<bool>()))
            .ReturnsAsync(dto is null
                ? Result<FinancialReportDto>.Failure("no")
                : Result<FinancialReportDto>.Success(dto));

        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        // The signature strip at the foot of the sheet reads the office's signatories.
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Head");

        return RenderComponent<SummaryDocument>();
    }

    [Fact]
    public void EverySectionOfTheReportIsOnTheSheet()
    {
        // The failure this page exists to end: the old export was assembled from the screen by a print stylesheet, so a
        // section the office had navigated away from was simply not in the document. This page is asked for the report and
        // states all of it, in one order, every time.
        var cut = RenderDocument(Report());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".doc-sec")), RenderTimeout);

        var sheet = cut.Markup;

        Assert.Contains("Collection position", sheet);
        Assert.Contains("Collection by model", sheet);
        Assert.Contains("Outstanding position", sheet);
        Assert.Contains("Receivable aging", sheet);
        Assert.Contains("Period comparison", sheet);
        Assert.Contains("Revenue by facility", sheet);
        Assert.Contains("Accounts needing follow-up", sheet);
        Assert.Contains("Miscellaneous", sheet);

        // The letterhead a government document carries, and the signatories it is signed under.
        Assert.Contains("Republic of the Philippines", sheet);
        Assert.Contains("Financial Summary", sheet);
        Assert.NotEmpty(cut.FindAll(".doc-head"));

        // Named in filing order, so a sheet quoted in a meeting can be pointed at by section.
        var titles = cut.FindAll(".doc-sec-title").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(8, titles.Count);
        Assert.StartsWith("I.", titles[0]);
        Assert.StartsWith("VIII.", titles[7]);
    }

    [Fact]
    public void TheSheetStatesFiguresAndNotExplanations()
    {
        // Asked for twice by the office: a filed financial document is read by people who know what a collection rate is.
        // Anything that explained how a figure was arrived at, or narrated which facility owed the most, is off the sheet —
        // the table already says so. What is left beside a table is a figure or a count.
        var cut = RenderDocument(Report());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".doc-sec")), RenderTimeout);

        var sheet = cut.Markup;

        Assert.DoesNotContain("Largest concentration", sheet);
        Assert.DoesNotContain("collected ÷", sheet);
        Assert.DoesNotContain("carry no monthly bill", sheet);
        Assert.DoesNotContain("partition", sheet);

        // And the lines that remain are short. Measured on collapsed whitespace, since the markup's indentation is not
        // something a reader sees. The longest is the movement line, which is figures with their base named.
        foreach (var line in cut.FindAll(".doc-line"))
        {
            var text = System.Text.RegularExpressions.Regex.Replace(line.TextContent, @"\s+", " ").Trim();
            Assert.True(text.Length <= 110, $"a line on the sheet runs long ({text.Length}): {text}");
        }
    }

    [Fact]
    public void TheFiguresAreTheReportsOwn_NotASecondSumOfThem()
    {
        // A filed document must not be a second opinion. Assessed, collected, unpaid and the rate are stated exactly as
        // the report gives them, and the model split is derived from the headline so its two halves add back to it.
        var cut = RenderDocument(Report());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".doc-sec")), RenderTimeout);

        var position = cut.Find(".doc-table-figures").TextContent;
        Assert.Contains("₱14,941", position);      // assessed
        Assert.Contains("₱2,749", position);       // collected
        Assert.Contains("₱12,192", position);      // unpaid
        Assert.Contains("18%", position);

        // Paid on service is SLH 1,230 + TPM 420 = 1,650; recurring is the remainder of the headline, 1,099.
        var model = cut.FindAll(".doc-sec")[1].TextContent;
        Assert.Contains("₱1,650", model);
        Assert.Contains("₱1,099", model);

        // Every delinquent account, not the capped list: the report states 7 while the fixture list carries 3.
        var followUp = cut.FindAll(".doc-sec").Single(s => s.TextContent.Contains("Accounts needing follow-up")).TextContent;
        Assert.Contains("7 accounts", followUp);
        Assert.Contains("Not defined. No age-based total is reported", followUp);
        Assert.Contains("Listing 3 of 7 accounts", followUp);
    }

    [Fact]
    public void AnEmptyBandOrAnEmptyListIsNotDrawn()
    {
        // The document states what is there. An aging band holding nothing, a period with no bill, and a follow-up list
        // with nobody on it would each be a row of noughts inviting a reader to wonder what it means.
        var cut = RenderDocument(Report() with
        {
            Aging = new List<ReceivableAgingBandDto> { new("1–2 months", 1, 3_600m) },
            Delinquent = new List<AttentionAccountDto>(),
            Arrears = null,
            DelinquentAccountsTotal = 0,
            DelinquentOutstandingTotal = 0m,
            ArrearsAccountsTotal = null,
            ArrearsOutstandingTotal = null,
            Misc = null
        });

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".doc-sec")), RenderTimeout);

        var sheet = cut.Markup;

        // One band states what the total above it already says, so the aging section is left out entirely.
        Assert.DoesNotContain("Receivable aging", sheet);
        Assert.DoesNotContain("Miscellaneous", sheet);

        // The follow-up section stays — its figures are the point — and says plainly that nobody is on the lists.
        Assert.Contains("Accounts needing follow-up", sheet);
        Assert.Contains("Arrears qualification", sheet);
        Assert.Contains("does not mean qualifying old/lapsed debt is absent", sheet);
        Assert.Contains("No account still being billed carries an unpaid month", sheet);
    }

    [Fact]
    public void TheSignatoriesAreLaidOutAcrossTheSheet_WithRoomToSign()
    {
        // The strip imposes no layout of its own — it hands its lines to whatever footer hosts it — and this document gave
        // it none, so the three lines stacked down the middle of the page. A signed sheet needs them across it, with space
        // above each name to sign and a rule to sign on.
        var cut = RenderDocument(Report());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".doc-sign")), RenderTimeout);

        var slots = cut.FindAll(".doc-sign .sig-slot");
        Assert.Equal(3, slots.Count);                       // prepared by, reviewed by, and the date prepared
        Assert.Contains("Date Prepared", cut.Find(".doc-sign").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSheetCarriesThePrintClass_SoThePageRuleReachesIt()
    {
        // The page margin is nought — which is what keeps the browser's date, title and URL off a document carrying the
        // municipal seal — and that rule is injected by the print helper against this class. Without it the sheet prints
        // with browser furniture on it.
        var cut = RenderDocument(Report());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".doc-sec")), RenderTimeout);

        Assert.NotEmpty(cut.FindAll(".print-report-sheet"));

        // The controls are not part of the document.
        Assert.NotEmpty(cut.FindAll(".doc-actions.no-print"));
    }

    [Fact]
    public void AReportThatCannotBeLoadedSaysSo_RatherThanPrintingABlankSheet()
    {
        var cut = RenderDocument(null);

        cut.WaitForAssertion(() => Assert.Contains("could not be prepared", cut.Markup), RenderTimeout);

        Assert.Empty(cut.FindAll(".doc-sec"));
    }
}
