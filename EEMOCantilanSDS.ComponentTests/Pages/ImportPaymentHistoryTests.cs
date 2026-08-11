using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Payments.BulkImportDailyHistory;
using EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Queries.Payments.GetCollectableDays;
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

    /// <summary>Stall identities, so a row can name the stall behind a number rather than the number alone.</summary>
    private static readonly Guid Stall1 = Guid.NewGuid();
    private static readonly Guid Stall2 = Guid.NewGuid();
    private static readonly Guid Space1 = Guid.NewGuid();

    /// <summary>
    /// The facility's own stalls, so the picker has something in it. Two numbered, one un-numbered space, and one
    /// closed — a closed stall's number must not be offered as a payor, and a space must be reachable by name because
    /// it has no number to type.
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
                        StallId = Stall1,
                        StallNo = "1", ActualOccupant = "George Giovanna",
                        EffectivityDate = PhilippineTime.Today.AddYears(-1), DurationYears = 3
                    },
                    new StallHolderRowDto
                    {
                        StallId = Stall2,
                        StallNo = "2", ActualOccupant = "Ackerman Tril",
                        EffectivityDate = PhilippineTime.Today.AddYears(-1), DurationYears = 3
                    },
                    new StallHolderRowDto
                    {
                        // A space the office does not number. Held without a contract, so its term is open-ended.
                        StallId = Space1,
                        StallNo = "SP-1", ActualOccupant = "Bernadette Lim",
                        Arrangement = OccupancyArrangement.SpaceOnly,
                        EffectivityDate = PhilippineTime.Today.AddYears(-1),
                        DurationYears = EEMOCantilanSDS.Domain.Constants.DomainRules.OpenEndedTermYears
                    },
                    new StallHolderRowDto
                    {
                        StallId = Guid.NewGuid(),
                        StallNo = "9", ActualOccupant = "Vacated Vendor", IsClosed = true,
                        EffectivityDate = PhilippineTime.Today.AddYears(-1), DurationYears = 3
                    }
                ]
            }
        ]
    };

    /// <summary>
    /// The days a stall still owes in a month, as the server states them.
    ///
    /// <para>Deliberately NOT the first days of the month: the vendor's space was let on the 9th, which is the very
    /// case that made a calendar-order prefill wrong. Two days are already collected, so the day still owed is the
    /// payor's THIRD market day — the number the office reckons by.</para>
    /// </summary>
    private static CollectableDaysDto Collectable(int year, int month) => new(
        Stall2, year, month,
        Uncollected: [new DateOnly(year, month, 9), new DateOnly(year, month, 10), new DateOnly(year, month, 11)],
        AlreadyCollected: 2,
        Excused: 0,
        ClosedOrOutsideTerm: 6,
        Chargeable:
        [
            new DateOnly(year, month, 7), new DateOnly(year, month, 8),
            new DateOnly(year, month, 9), new DateOnly(year, month, 10), new DateOnly(year, month, 11)
        ]);

    private bool _registered;

    private IRenderedComponent<ImportPaymentHistory> Render(string facility)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        if (_registered)
        {
            // bUnit refuses new registrations once anything has been resolved, and one test renders two facilities to
            // show that an option offered for the market is not offered elsewhere.
            return RenderComponent<ImportPaymentHistory>(p => p.Add(c => c.Facility, facility));
        }

        _registered = true;

        _payments = new Mock<IPaymentsApiClient>();

        // Both imports answer with an empty-but-successful result. Without this the page would be testing a null the
        // real HTTP client never returns.
        _payments.Setup(p => p.ImportPaymentHistoryAsync(It.IsAny<BulkImportPaymentHistoryCommand>()))
                 .ReturnsAsync(Result<BulkImportPaymentResultDto>.Success(
                     new BulkImportPaymentResultDto(0, 0, 0, 0, 0, 0m, [])));
        _payments.Setup(p => p.ImportDailyHistoryAsync(It.IsAny<BulkImportDailyHistoryCommand>()))
                 .ReturnsAsync(Result<BulkImportDailyResultDto>.Success(
                     new BulkImportDailyResultDto(0, 0, 0, 0, 0, 0, 0m, [])));

        // The day prefill reads the payor's own uncollected days from the server, so the fixture has to answer.
        _payments.Setup(p => p.GetCollectableDaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((Guid _, int y, int m) =>
                     Result<CollectableDaysDto>.Success(Collectable(y, m)));

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
    public void TheMarketAsksForItsSectionBeforeAnythingElse()
    {
        var cut = Render("npm");

        // The market numbers its spaces PER SECTION - three of them are called "1" - so a list without a section could
        // be written against the wrong vendor's space. Asked before a file is read rather than after.
        Assert.Contains("Which section are you importing?", cut.Markup);
        Assert.DoesNotContain("Choose a file to upload", cut.Markup);
        Assert.Equal(3, cut.FindAll("button.iph-pick-card").Count);
    }

    [Fact]
    public void TheMarketAsksForDaysRatherThanAnAmount()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        // A month at the market is not a payment; it is a run of market days at a fixed daily fee. There is nothing
        // for the office to total, because the money follows from the facility's own rate for the days settled.
        Assert.Contains("Days Paid", cut.Markup);
        Assert.DoesNotContain("Amount Paid", cut.Markup);
        Assert.Single(cut.FindAll("input.iph-days"));

        // And no date-paid column: the days ARE the dates.
        Assert.DoesNotContain("Date Paid", cut.Markup);
    }

    [Fact]
    public void TheMarketSavesThroughItsOwnImportAndNotTheMonthlyOne()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[1].Click();          // Fish Section
        cut.Find("button.iph-use-sample").Click();

        cut.Find("button.iph-btn-primary").Click();

        // Sent as days through the market's own import. Through the monthly one it would have recorded a month of
        // daily fees as a single monthly payment and settled days nobody collected.
        _payments.Verify(p => p.ImportDailyHistoryAsync(It.Is<BulkImportDailyHistoryCommand>(c =>
            c.FacilityCode == FacilityCode.NPM
            && c.Section == MarketSection.FishSection
            && c.Rows.All(r => r.DaysPaid > 0))), Times.Once);

        _payments.Verify(
            p => p.ImportPaymentHistoryAsync(It.IsAny<BulkImportPaymentHistoryCommand>()),
            Times.Never);
    }

    [Fact]
    public void TheExactDatesGridAppearsOnlyWhenAskedFor()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        // Off by default: most sheets record a count, and a grid of dates nobody fills in is a grid of mistakes.
        Assert.Empty(cut.FindAll("tr.iph-day-row"));

        cut.Find("button.iph-dates-toggle").Click();
        Assert.NotEmpty(cut.FindAll("tr.iph-day-row"));

        // And it is only offered for the market. A monthly row already names its own date paid.
        var monthly = Render("tcc");
        monthly.Find("button.iph-enter-manually").Click();
        Assert.Empty(monthly.FindAll("button.iph-dates-toggle"));
    }

    [Fact]
    public void TheDatesOfferedAreThePayorsOwnUncollectedDays_NotTheFirstDaysOfTheMonth()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("3");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // The server says this vendor owes the 9th, 10th and 11th - its space was let on the 9th. Filling 1, 2, 3 from
        // the calendar offered days the vendor never owed, which is the fault this replaced.
        var filled = cut.FindAll("input.iph-date-slot")
            .Select(s => s.GetAttribute("value") ?? string.Empty)
            .ToList();

        Assert.Equal(3, filled.Count);
        Assert.Equal(
            [$"{past.Year:0000}-{past.Month:00}-09", $"{past.Year:0000}-{past.Month:00}-10", $"{past.Year:0000}-{past.Month:00}-11"],
            filled);
    }

    [Fact]
    public void ARowWithNoPayorIsToldToChooseOneRatherThanGuessedAt()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.iph-dates-toggle").Click();

        // Which days a vendor owes depends on which vendor. Without one, the screen says so instead of offering the
        // month's first days as though they were owed.
        Assert.Contains("Choose the payor first", cut.Markup);
        Assert.All(cut.FindAll("input.iph-date-slot"),
            s => Assert.True(string.IsNullOrEmpty(s.GetAttribute("value"))));
    }

    [Fact]
    public void ChangingTheDayCountResizesTheGridWithoutLosingWhatWasTyped()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("3");
        cut.Find("button.iph-dates-toggle").Click();

        var firstBefore = cut.FindAll("input.iph-date-slot")[0].GetAttribute("value");

        cut.Find("input.iph-days").Input("5");
        Assert.Equal(5, cut.FindAll("input.iph-date-slot").Count);

        // Growing the count must not disturb the days already stated.
        Assert.Equal(firstBefore, cut.FindAll("input.iph-date-slot")[0].GetAttribute("value"));

        cut.Find("input.iph-days").Input("2");
        Assert.Equal(2, cut.FindAll("input.iph-date-slot").Count);
        Assert.Equal(firstBefore, cut.FindAll("input.iph-date-slot")[0].GetAttribute("value"));
    }

    [Fact]
    public void WhenTheOfficeStatesNoDates_NoneAreSent()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.FindAll("input.iph-input").First(i => i.GetAttribute("aria-label") == "OR number").Input("OR-500");

        cut.Find("button.iph-btn-primary").Click();

        // Nothing stated, so the server fills the month's collectable days - which is the only defensible reading of a
        // sheet that records a count.
        _payments.Verify(p => p.ImportDailyHistoryAsync(It.Is<BulkImportDailyHistoryCommand>(c =>
            c.Rows.All(r => r.Days == null || r.Days.Count == 0))), Times.Once);
    }

    [Fact]
    public void WhenTheOfficeStatesTheDates_ExactlyThoseAreSent()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.FindAll("input.iph-input").First(i => i.GetAttribute("aria-label") == "OR number").Input("OR-500");
        cut.Find("button.iph-dates-toggle").Click();

        cut.Find("button.iph-btn-primary").Click();

        // Exactly those days travel, and the import honours them without filling anything in around them.
        _payments.Verify(p => p.ImportDailyHistoryAsync(It.Is<BulkImportDailyHistoryCommand>(c =>
            c.Rows.All(r => r.Days != null && r.Days.Count == 2
                            && r.Days.All(d => d.Date.Year == past.Year && d.Date.Month == past.Month)))), Times.Once);
    }

    [Fact]
    public void EachDayIsARowOfTheSameTable_RepeatingTheOccupantAndPeriodWithoutOfferingThemForEditing()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // Rows of the SAME table, not a table inside it. A nested one is measured against its own contents, so it sat
        // narrower than the row it belonged to with a band of empty space down the right.
        Assert.Empty(cut.FindAll("table.iph-days-table"));

        // Two days, and each line carries the row's full column count so it lands under the headings that describe it.
        var dayRows = cut.FindAll("tr.iph-day-row:not(.iph-day-caption)");
        Assert.Equal(2, dayRows.Count);
        Assert.All(dayRows, r => Assert.Equal(7, r.QuerySelectorAll("td").Length));

        // Stated, not offered for editing: they belong to the row above, and a second editable copy of a value is a
        // second version of it.
        Assert.All(dayRows, r =>
        {
            var read = r.QuerySelectorAll("td.iph-days-read").Select(c => c.TextContent.Trim()).ToList();
            Assert.Contains("Ackerman Tril", read);
            Assert.Contains($"{past.Year:0000}-{past.Month:00}", read);
        });

        // One date and one receipt per line, in the columns the row above uses for the same things.
        Assert.All(dayRows, r =>
        {
            Assert.Single(r.QuerySelectorAll("input.iph-date-slot"));
            Assert.Single(r.QuerySelectorAll("input.iph-day-or"));
        });
    }

    [Fact]
    public void ASettledMonthSaysSoInsteadOfOfferingEmptyBoxes()
    {
        // Every day of the month already collected. Empty date fields with no explanation invited the office to guess,
        // and a guess here records a collection that did not happen.
        var cut = Render("npm");

        // Set AFTER rendering: Render creates the mock, so an override placed before it is replaced.
        _payments.Setup(p => p.GetCollectableDaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((Guid s, int y, int m) =>
                     Result<CollectableDaysDto>.Success(new CollectableDaysDto(s, y, m, [], 22, 0, 8)));

        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        Assert.Contains("Paid up to date", cut.Markup);
        Assert.Contains("22 market days", cut.Markup);
    }

    [Fact]
    public void MoreDaysClaimedThanAreUncollectedIsSaidPlainly()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("5");        // the fixture offers three
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // Three dated, two left undated, and the reason stated rather than left to be inferred from empty boxes.
        Assert.Contains("3 days are uncollected", cut.Markup);
        Assert.Equal(3, cut.FindAll("input.iph-date-slot").Count(s => !string.IsNullOrEmpty(s.GetAttribute("value"))));
    }

    [Fact]
    public void ARowThatCannotBeRecordedIsRefusedBeforeTheRequest()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        // A period that cannot be read as a month. The office hears it once, by count, rather than as a list of
        // rejections read back afterwards.
        cut.Find("input.iph-period").Input("not-a-month");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.iph-btn-primary").Click();

        Assert.Contains("does not name a month", cut.Markup);
        _payments.Verify(
            p => p.ImportDailyHistoryAsync(It.IsAny<BulkImportDailyHistoryCommand>()),
            Times.Never);
    }

    [Fact]
    public void PastedNonsenseCannotOverrunTheCells()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        // A cell with no limit took a hundred characters of nonsense, which then stood in every day line beneath it and
        // pushed the table sideways.
        var occupant = cut.FindAll("input.iph-input").First(i => i.GetAttribute("aria-label") == "Actual occupant");
        Assert.Equal("120", occupant.GetAttribute("maxlength"));

        var or = cut.FindAll("input.iph-input").First(i => i.GetAttribute("aria-label") == "OR number");
        Assert.Equal("40", or.GetAttribute("maxlength"));

        Assert.Equal("7", cut.Find("input.iph-period").GetAttribute("maxlength"));
    }

    [Fact]
    public void AReceiptPerDayIsEnough_TheMonthDoesNotNeedOneOfItsOwn()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // What the office actually does: a receipt against each day, none for the month, because the market issues one
        // per collection. This was refused outright - the whole row, with both of its receipts - and nothing recorded.
        var dayOrs = cut.FindAll("input.iph-day-or");
        Assert.Equal(2, dayOrs.Count);
        dayOrs[0].Input("77756724");
        cut.FindAll("input.iph-day-or")[1].Input("6454645");

        cut.Find("button.iph-btn-primary").Click();

        _payments.Verify(p => p.ImportDailyHistoryAsync(It.Is<BulkImportDailyHistoryCommand>(c =>
            c.Rows.All(r => string.IsNullOrWhiteSpace(r.OrNumber)
                            && r.Days != null
                            && r.Days.Count == 2
                            && r.Days.All(d => !string.IsNullOrWhiteSpace(d.OrNumber))))), Times.Once);
    }

    [Fact]
    public void ADayWithNoReceiptAnywhereIsRefusedBeforeTheRequest()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}-{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // One day given a receipt, the other left blank, and no receipt on the month either.
        cut.FindAll("input.iph-day-or")[0].Input("77756724");
        cut.Find("button.iph-btn-primary").Click();

        Assert.Contains("no OR number", cut.Markup);
        _payments.Verify(
            p => p.ImportDailyHistoryAsync(It.IsAny<BulkImportDailyHistoryCommand>()),
            Times.Never);
    }

    [Fact]
    public void ThePeriodTakesItsOwnDash()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        // Six keystrokes, not seven: the office types digits and the shape follows. A period written in a shape the
        // import cannot read is a rejection the clerk only hears about after saving.
        var period = cut.Find("input.iph-period");

        period.Input("2026");
        Assert.Equal("2026", cut.Find("input.iph-period").GetAttribute("value"));

        period = cut.Find("input.iph-period");
        period.Input("20264");
        Assert.Equal("2026-4", cut.Find("input.iph-period").GetAttribute("value"));

        period = cut.Find("input.iph-period");
        period.Input("202604");
        Assert.Equal("2026-04", cut.Find("input.iph-period").GetAttribute("value"));
    }

    [Fact]
    public void ATypedDashIsNotDoubled()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        // An office that types the dash out of habit must not end up with "2026--4". Only the digits are kept, so the
        // shape is the same whichever way it was typed.
        cut.Find("input.iph-period").Input("2026-04");
        Assert.Equal("2026-04", cut.Find("input.iph-period").GetAttribute("value"));
    }

    [Fact]
    public void UndatedDayLinesAreNamedAsTheFault_NotTheReceipt()
    {
        // The office wrote a receipt against every line and left the dates blank, because that month had no day the
        // payor owed. The complaint came back as "an OR number is required" - at an office that had entered three of
        // them. The fault is the dates, and the message must say so.
        var cut = Render("npm");

        _payments.Setup(p => p.GetCollectableDaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((Guid s, int y, int m) =>
                     Result<CollectableDaysDto>.Success(new CollectableDaysDto(s, y, m, [], 0, 0, 30)));

        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}{past.Month:00}");
        cut.Find("input.iph-days").Input("3");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // Receipts written, dates blank - exactly the office's entry.
        cut.FindAll("input.iph-day-or")[0].Input("445645646");
        cut.FindAll("input.iph-day-or")[1].Input("465456465");
        cut.FindAll("input.iph-day-or")[2].Input("5647347457");

        cut.Find("button.iph-btn-primary").Click();

        Assert.Contains("with no date", cut.Markup);
        Assert.DoesNotContain("OR number is required", cut.Markup);
        _payments.Verify(
            p => p.ImportDailyHistoryAsync(It.IsAny<BulkImportDailyHistoryCommand>()),
            Times.Never);
    }

    [Fact]
    public void ADayLineIsNumberedByThePayorsOwnMarketDay_NotTheLineOfTheForm()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // The payor's chargeable days start on the 7th, so the 9th is their third and the 10th their fourth. The lines
        // said "Day 1" and "Day 2", which described this form rather than the vendor the office reconciles against.
        var labels = cut.FindAll("td.iph-day-label").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(["Day 3", "Day 4"], labels);
    }

    [Fact]
    public void AnUndatedLineClaimsNoDayNumber()
    {
        var cut = Render("npm");

        _payments.Setup(p => p.GetCollectableDaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((Guid s, int y, int m) =>
                     Result<CollectableDaysDto>.Success(new CollectableDaysDto(s, y, m, [], 0, 0, 30, [])));

        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        // No date, so there is no day of the payor's to name. Saying "Day 1" would be a number about nothing.
        Assert.All(cut.FindAll("td.iph-day-label"), c => Assert.Equal("—", c.TextContent.Trim()));
    }

    [Fact]
    public void TypingThePeriodDoesNotAskTheServerOnEveryKeystrokeOrWipeADateAlreadyEntered()
    {
        var cut = Render("npm");
        cut.FindAll("button.iph-pick-card")[0].Click();
        cut.Find("button.iph-enter-manually").Click();

        var past = PhilippineTime.Today.AddMonths(-3);
        cut.Find("input.iph-period").Input($"{past.Year:0000}{past.Month:00}");
        cut.Find("input.iph-days").Input("2");
        cut.Find("button.pp-field").Click();
        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();
        cut.Find("button.iph-dates-toggle").Click();

        var dated = cut.FindAll("input.iph-date-slot")[0].GetAttribute("value");
        Assert.False(string.IsNullOrEmpty(dated));

        _payments.Invocations.Clear();

        // Re-typing the SAME month, digit by digit. Bound to oninput, this used to clear the dates and ask the server
        // once per keystroke - so a date entered by hand was wiped while the clerk typed the month beside it.
        cut.Find("input.iph-period").Input($"{past.Year:0000}{past.Month:00}");

        Assert.Equal(dated, cut.FindAll("input.iph-date-slot")[0].GetAttribute("value"));
        _payments.Verify(
            p => p.GetCollectableDaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public void AFacilityChargedPerUnitHasNoHistoryToImport()
    {
        var cut = Render("slh");

        // Per head, per trip - there is no period to key a history to. The office is told why rather than finding the
        // option simply absent, which would read as an oversight and invite someone to force it through another route.
        Assert.Contains("per unit collected", cut.Markup);
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
    public void TheOfficeCanSearchAndPickAPayorInsteadOfRememberingAStallNumber()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        // Nothing is chosen yet, so the control says so rather than showing a number that means nothing.
        Assert.Contains("Choose payor", cut.Markup);

        cut.Find("button.pp-field").Click();

        // The facility's own vendors, by name. A closed stall is not a payor and must not be offered.
        var options = cut.FindAll("button.pp-opt");
        Assert.Equal(3, options.Count);
        Assert.Contains("George Giovanna", cut.Markup);
        Assert.DoesNotContain("Vacated Vendor", cut.Markup);
    }

    [Fact]
    public void AVendorWithNoStallIsReachableByNameAndNeverShownAnInternalNumber()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();
        cut.Find("button.pp-field").Click();

        // Searching by name is the ONLY way to reach a vendor the office does not number - there is no number to type.
        cut.Find("input.pp-search-input").Input("bernad");

        var match = Assert.Single(cut.FindAll("button.pp-opt"));
        Assert.Contains("Bernadette Lim", match.InnerHtml);

        // SP-1 is an internal key. It has never appeared on the office's own list and must not appear here, or the
        // staff would learn a stall number that does not exist.
        Assert.DoesNotContain("SP-1", cut.Markup);
        Assert.DoesNotContain("SP&#8209;1", cut.Markup);
        Assert.Contains("no stall no.", match.InnerHtml);
    }

    [Fact]
    public void PickingAPayorWritesTheRealNameOntoTheRow()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();
        cut.Find("button.pp-field").Click();

        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Ackerman Tril")).Click();

        // The name used to be shown as a placeholder, which looked filled in and saved as blank. It is now the input's
        // actual value, so what the office sees is what the import records.
        var occupant = cut.FindAll("input.iph-input").First(i => i.GetAttribute("aria-label") == "Actual occupant");
        Assert.Equal("Ackerman Tril", occupant.GetAttribute("value"));

        // And the closed control states the choice rather than a placeholder.
        Assert.Contains("Stall 2 · Ackerman Tril", cut.Find("button.pp-field").TextContent);
    }

    [Fact]
    public void PickingASpaceStatesTheVendorWithoutInventingAStallNumber()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();
        cut.Find("button.pp-field").Click();

        cut.FindAll("button.pp-opt").First(o => o.InnerHtml.Contains("Bernadette Lim")).Click();

        var field = cut.Find("button.pp-field").TextContent;
        Assert.Contains("Bernadette Lim", field);
        Assert.DoesNotContain("Stall", field);
        Assert.DoesNotContain("SP-1", field);
    }

    [Fact]
    public void ANumberCanStillBeTypedForAStallNobodyOccupiesToday()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();
        cut.Find("button.pp-field").Click();

        // The escape hatch: an office working from an older sheet may need a number that is not let today.
        cut.Find("input.pp-manual").Input("47");
        cut.Find("button.pp-manual-btn").Click();

        Assert.Contains("Stall 47", cut.Find("button.pp-field").TextContent);
    }

    [Fact]
    public void SearchingSomethingThatIsNotThereSaysSoRatherThanShowingAnEmptyList()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();
        cut.Find("button.pp-field").Click();

        cut.Find("input.pp-search-input").Input("zzz");

        Assert.Empty(cut.FindAll("button.pp-opt"));
        Assert.Contains("Nothing matches", cut.Markup);
    }

    [Fact]
    public void ThePayorListIsADialogSoTheScrollingTableCannotClipIt()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();
        cut.Find("button.pp-field").Click();

        // It opened as a panel hanging off the field, inside a table that scrolls sideways - so it was cut off two
        // rows down and appeared to sit behind the table. Fixed to the viewport, nothing can clip it.
        var dialog = cut.Find(".pp-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Single(cut.FindAll(".pp-scrim"));
    }

    [Fact]
    public void TheDialogClosesOnEscapeAndOnTheScrim()
    {
        var cut = Render("tcc");
        cut.Find("button.iph-enter-manually").Click();

        cut.Find("button.pp-field").Click();
        cut.Find(".pp-dialog").KeyDown(Key.Escape);
        Assert.Empty(cut.FindAll(".pp-dialog"));

        cut.Find("button.pp-field").Click();
        cut.Find(".pp-scrim").Click();
        Assert.Empty(cut.FindAll(".pp-dialog"));
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
