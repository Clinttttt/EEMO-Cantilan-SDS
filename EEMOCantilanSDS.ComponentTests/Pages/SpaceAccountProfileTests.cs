using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The profile of a Kanmanggay, Vegetable/Fruit or Fiesta/Araw account is the stall profile's own page: a hero, field cards, a 12-month grid and a
/// collection history. It is keyed by the account's source id from the route (never a name), keeps the occupant and the signed-contract lessee
/// apart, shows the contract's stated rate as evidence beside the approved amount, and never invents a value that was not recorded.
/// </summary>
public sealed class SpaceAccountProfileTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IObligationsApiClient> _accounts = new();
    private static DateOnly ThisMonth => new(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1);

    public SpaceAccountProfileTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Head").SetRoles("SuperAdmin");
        Services.AddSingleton(_accounts.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>(); Services.AddSingleton<FacilityState>(new FacilityState(Mock.Of<IFacilitiesApiClient>()));
    }

    private static ObligationAccountDto Signed(ObligationKind kind = ObligationKind.KanmanggaySpaceRental, string occupant = "Elena Ramos") => new(
        Guid.NewGuid(), kind, kind.ToString(), Guid.NewGuid(), occupant, null, null, "7", null, null, new(2026, 2, 1), null, 900m, new(2026, 2, 1),
        1800m, 400m, 1400m, OccupancyArrangement.SignedContract, "KMG-007", occupant, "Estate of Elena Ramos", new(2026, 2, 1), 3, 9.5m, "Row A", 1000m);

    private static ObligationQuoteDto Period(ObligationAccountDto account, DateOnly start, decimal assessed, decimal settled) =>
        new(account.Id, Guid.NewGuid(), account.Kind, account.KindLabel, account.SubjectLabel, start, assessed, settled, assessed - settled, account.PayorId, account.PayerName, null, true);

    private IRenderedComponent<SpaceAccountProfile> Open(ObligationAccountDto account, string slug, IReadOnlyList<ObligationQuoteDto>? periods = null,
        IReadOnlyList<ObligationPaymentDto>? payments = null, params ObligationAccountDto[] others)
    {
        _accounts.Setup(x => x.GetAccountsAsync(account.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([account, .. others]));
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success(periods ?? []));
        _accounts.Setup(x => x.GetPaymentsAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationPaymentDto>>.Success(payments ?? []));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/operations/{slug}/profile/{account.Id}");
        return RenderComponent<SpaceAccountProfile>(p => p.Add(x => x.AccountId, account.Id));
    }

    [Fact]
    public void TheProfileKeepsTheOccupantAndTheSignedLesseeApart_AndStatesWhatWasRecordedWithoutInventing()
    {
        var account = Signed();
        var cut = Open(account, "kanmanggay", [Period(account, ThisMonth, 900m, 400m)]);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Elena Ramos", cut.Find("h1.prof-hero-name").TextContent.Trim());                 // the person is the title
            var text = cut.Find(".rental-profile").TextContent;
            Assert.Contains("Estate of Elena Ramos", text);                                                 // the signed lessee is its own line
            Assert.Contains("February 1, 2026", text);                                                       // effectivity
            Assert.Contains("3 years", text);
            Assert.Contains("January 31, 2029", text);                                                        // expiry from the recorded term (the day before the anniversary), not guessed
            Assert.Contains("9.50 sqm", text);
            Assert.Contains("₱900/month", text);                                                             // what is assessed
            Assert.Contains("₱1,000/month", text);                                                           // what the contract states, kept beside it
            Assert.Contains("₱10,800", text);                                                                // whole year = 12 x the approved monthly, labelled as such
            Assert.Contains("12 × approved monthly", text);
            Assert.DoesNotContain("KanmanggaySpaceRental", text);                                            // no internal source name or id on the page
            Assert.DoesNotContain(account.Id.ToString(), cut.Markup.Replace("href=", ""));
        }, Timeout);
    }

    [Fact]
    public void AnAccountWithoutAContractShowsNoContractOnFile_NotAnInventedLessee()
    {
        var account = Signed(ObligationKind.VegetableFruitSpaceRental) with { Arrangement = OccupancyArrangement.SpaceOnly, ContractReference = null, ContractName = null, ContractEffectiveDate = null,
            ContractYears = null, AreaSqm = null, AreaLocation = null, ContractMonthlyRate = null };
        var cut = Open(account, "vegetable-fruit");

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find(".rental-profile").TextContent;
            Assert.Contains("No contract on file", text);
            Assert.Contains("No contract (space only)", text);
            Assert.Contains("Not recorded", text);                                                           // the unrecorded contract rate
            Assert.DoesNotContain("years", text);
        }, Timeout);
    }

    [Fact]
    public void TheMonthGridShowsEachPeriodsOwnState_AndFutureAndPreAccountMonthsAreNeverUnpaid()
    {
        var account = Signed();
        var year = PhilippineTime.Today.Year;
        var cut = Open(account, "kanmanggay", [Period(account, new(year, 2, 1), 900m, 900m), Period(account, new(year, 3, 1), 900m, 300m), Period(account, new(year, 4, 1), 900m, 0m)]);

        cut.WaitForAssertion(() =>
        {
            var tiles = cut.FindAll(".prof-hist-month");
            Assert.Equal(12, tiles.Count);
            Assert.Contains("hist-na", tiles[0].ClassName);                                                  // January: before the account opened in February
            Assert.Contains("hist-paid", tiles[1].ClassName);
            Assert.Contains("hist-partial", tiles[2].ClassName);
            Assert.Contains("hist-unpaid", tiles[3].ClassName);
            if (year == PhilippineTime.Today.Year && PhilippineTime.Today.Month < 12)
                Assert.Contains("hist-future", tiles[11].ClassName);                                         // December has not come due
            Assert.All(tiles, t => Assert.False(string.IsNullOrWhiteSpace(t.GetAttribute("aria-label"))));   // never colour alone
        }, Timeout);
    }

    [Fact]
    public void CollectionHistoryIsThisAccountsOwn_WithItsSrc_AndAReversalKeepsItsAuditNote()
    {
        var account = Signed(); var sameName = Signed(occupant: "Elena Ramos");                               // the same person's other space
        var live = new ObligationPaymentDto(Guid.NewGuid(), "SRC-2026-000042", PhilippineTime.Today, DateTime.UtcNow, ThisMonth, 400m, 0m, 400m,
            RevenueInstrumentType.OfficialReceipt, "Pat Collector", "Posted");
        var reversed = new ObligationPaymentDto(Guid.NewGuid(), "SRC-2026-000041", PhilippineTime.Today, DateTime.UtcNow.AddMinutes(-9), ThisMonth, 300m, -300m, 0m,
            RevenueInstrumentType.OfficialReceipt, "Pat Collector", "Reversed", [new("Reversal", "Wrong amount", PhilippineTime.Today, DateTime.UtcNow, -300m)]);
        var cut = Open(account, "kanmanggay", [Period(account, ThisMonth, 900m, 400m)], [live, reversed], sameName);

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".prof-ch-table tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("SRC-2026-000042", rows[0].TextContent);
            Assert.Contains("SRC-2026-000041", rows[1].TextContent);                                         // a reversed collection keeps its SRC
            Assert.Contains("Reversed", rows[1].TextContent);
            Assert.Contains("Wrong amount", rows[1].TextContent);
            Assert.Contains("Pat Collector", rows[0].TextContent);
        }, Timeout);
        _accounts.Verify(x => x.GetPaymentsAsync(account.Id), Times.Once);
        _accounts.Verify(x => x.GetPaymentsAsync(sameName.Id), Times.Never);                                   // the other space of the same person is never read
    }

    [Fact]
    public void AnEventLotProfileHasNoMonthlyContractFieldsAndNoMonthGrid()
    {
        var lot = new ObligationAccountDto(Guid.NewGuid(), ObligationKind.FiestaArawLotRental, "Fiesta / Araw Lot Rental", Guid.NewGuid(), "Ramon Bautista", null, null, "L-4",
            LotRentalEvent.Fiesta, new(2026, 10, 12), new(2026, 10, 12), null, 1200m, new(2026, 10, 12), 1200m, 0m, 1200m, ActualOccupant: "Ramon Bautista");
        var cut = Open(lot, "fiesta-araw", [new(lot.Id, Guid.NewGuid(), lot.Kind, lot.KindLabel, "L-4", new(2026, 10, 12), 1200m, 0m, 1200m, lot.PayorId, "Ramon Bautista", null, true, LotRentalEvent.Fiesta)]);

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find(".rental-profile").TextContent;
            Assert.Contains("Fiesta", text);
            Assert.Contains("October 12, 2026", text);
            Assert.Contains("₱1,200.00", text);
            Assert.DoesNotContain("Contract Duration", text);                                                // no invented three-year contract
            Assert.DoesNotContain("Whole Year", text);
            Assert.Empty(cut.FindAll(".prof-hist-month"));
            Assert.Empty(cut.FindAll(".ph-year-nav"));
        }, Timeout);
    }

    [Fact]
    public void AnUnknownAccountIsReportedAsNotFound_NeverSubstitutedByAnotherWithTheSameName()
    {
        var wanted = Signed(); var other = Signed(occupant: "Elena Ramos");
        _accounts.Setup(x => x.GetAccountsAsync(wanted.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([other]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/operations/kanmanggay/profile/{wanted.Id}");

        var cut = RenderComponent<SpaceAccountProfile>(p => p.Add(x => x.AccountId, wanted.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("was not found", cut.Markup);
            Assert.Empty(cut.FindAll(".rental-profile"));
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/kanmanggay");
        }, Timeout);
        _accounts.Verify(x => x.GetRegisterAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void ALoadFailureIsStatedWithARetry_AndRetryReloads()
    {
        var account = Signed();
        _accounts.SetupSequence(x => x.GetAccountsAsync(account.Kind))
            .ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Failure("Unavailable"))
            .ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([account]));
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        _accounts.Setup(x => x.GetPaymentsAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationPaymentDto>>.Success([]));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/operations/kanmanggay/profile/{account.Id}");

        var cut = RenderComponent<SpaceAccountProfile>(p => p.Add(x => x.AccountId, account.Id));
        cut.WaitForAssertion(() => Assert.Contains("Unavailable", cut.Find("[role=alert]").TextContent), Timeout);

        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => Assert.Equal("Elena Ramos", cut.Find("h1.prof-hero-name").TextContent.Trim()), Timeout);
    }

    [Fact]
    public void WhenOnlyThePaymentReadFails_TheProfileStillShowsAndSaysSo()
    {
        var account = Signed();
        _accounts.Setup(x => x.GetAccountsAsync(account.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([account]));
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success([]));
        _accounts.Setup(x => x.GetPaymentsAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationPaymentDto>>.Failure("down"));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/operations/kanmanggay/profile/{account.Id}");

        var cut = RenderComponent<SpaceAccountProfile>(p => p.Add(x => x.AccountId, account.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Elena Ramos", cut.Find("h1.prof-hero-name").TextContent.Trim());
            Assert.Contains("Collection history could not be loaded", cut.Find(".prof-ch-state").TextContent);   // never an empty "No collections recorded"
            Assert.DoesNotContain("No collections recorded yet", cut.Markup);
        }, Timeout);
    }
}
