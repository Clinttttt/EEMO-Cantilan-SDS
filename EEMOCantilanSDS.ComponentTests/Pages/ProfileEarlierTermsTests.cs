using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Profile = EEMOCantilanSDS.Client.Components.Pages.Shared.Actions.Profile;

/// <summary>
/// The "Earlier Terms on This Stall" card, which had no test because no fixture existed to render the profile.
///
/// <para>
/// This card is where the office is told that money is owed by a lessee who is NOT the one in the stall now. A market space is
/// re-let, and each term's arrears belong to the person who incurred them — the register is explicit that an earlier term's
/// balance is not part of the current term's. So what the card must never do is offer to record a collection against a stranger's
/// term, and what it must never fail to do is offer it for a term whose period has run out while the lessee remains.
/// </para>
///
/// <para>
/// A non-NPM facility is used deliberately: the market profile also builds a daily heat-map and fetches resolved rates, none of
/// which this card touches, and rendering them would make the fixture describe the wrong thing.
/// </para>
/// </summary>
public class ProfileEarlierTermsTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    private static readonly Guid StallId = Guid.NewGuid();
    private static readonly Guid FirstTermId = Guid.NewGuid();
    private static readonly Guid SecondTermId = Guid.NewGuid();

    /// <summary>An earlier occupancy of this same stall, with its own balance.</summary>
    private static ClosedStallAccountDto Term(
        string occupant,
        decimal uncollected,
        InactiveAccountState state = InactiveAccountState.Superseded,
        Guid? contractId = null,
        decimal lifetimeCollected = 12_000m) =>
        new(StallId, state, FacilityCode.TCC, "Tampak Commercial Center", "4", occupant, occupant,
            new DateOnly(2023, 1, 1), 3, 900m, null, new DateOnly(2026, 1, 1),
            lifetimeCollected, uncollected, null, "", new DateOnly(2026, 1, 1), false,
            contractId ?? FirstTermId);

    private IRenderedComponent<Profile> RenderProfile(string currentOccupant, params ClosedStallAccountDto[] priorTerms)
    {
        this.AddTestAuthorization().SetAuthorized("cly.sullano");

        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallsByFacilityPaginatedAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Result<CursorPagedResult<StallDto>>.Success(new CursorPagedResult<StallDto>
            {
                Items = new List<StallDto> { Stall(currentOccupant) },
                NextCursor = null,
                HasMore = false,
            }));
        stalls.Setup(s => s.GetClosedStallAccountsAsync())
            .ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success(priorTerms));
        stalls.Setup(s => s.GetStallCollectionHistoryAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Result<CursorPagedResult<StallCollectionHistoryRowDto>>.Failure("not needed here"));
        stalls.Setup(s => s.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Failure("not a market stall"));

        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(p => p.GetPaymentHistoryAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success(Array.Empty<PaymentHistoryDto>()));
        payments.Setup(p => p.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<PaymentRecordDto>.NotFound());
        payments.Setup(p => p.GetStallLedgerSummaryAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result<StallLedgerSummaryDto>.Failure("not needed here"));

        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(payments.Object);
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();

        return RenderComponent<Profile>(p => p
            .Add(c => c.FacilityId, "TCC")
            .Add(c => c.StallKey, StallId.ToString()));
    }

    private static StallDto Stall(string occupant) => new(
        Id: StallId, StallNo: "4", Status: StallStatus.Active, ActualOccupant: occupant, NameOnContract: occupant,
        AreaSqm: null, ContractDate: new DateTime(2024, 1, 1), MonthlyRate: 900m, DailyRate: null, ORNumber: null,
        Section: null, AreaLocation: null, AreaNote: null, Remarks: null, ContractYears: 3);

    [Fact]
    public void AnEarlierTermIsListedWithItsOwnOccupantAndItsOwnBalance()
    {
        var page = RenderProfile("Ana Reyes", Term("Rosa Magbanua", 4_800m));

        page.WaitForAssertion(() =>
        {
            var card = page.Find(".prof-prior");
            Assert.Contains("Rosa Magbanua", card.TextContent);
            Assert.Contains("4,800", card.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void AStrangersEndedTermIsNotOfferedForCollection()
    {
        // The protection that matters. Rosa's term ended and Ana holds the stall now; offering "record payment on this term"
        // here would invite the clerk to post Ana's money onto Rosa's account.
        var page = RenderProfile("Ana Reyes", Term("Rosa Magbanua", 4_800m));

        page.WaitForAssertion(() => Assert.Contains("Rosa Magbanua", page.Markup), RenderTimeout);
        Assert.Empty(page.FindAll("button.prof-prior-pay"));
    }

    [Fact]
    public void ATermWhosePeriodRanOutWhileTheLesseeRemains_IsOfferedForCollection()
    {
        // The opposite case, and the reason the card exists at all: a lapsed term IS the term in force, so its balance is
        // still collectable and the office needs to be able to record against it.
        var page = RenderProfile("Rosa Magbanua",
            Term("Rosa Magbanua", 4_800m, InactiveAccountState.Lapsed));

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll("button.prof-prior-pay")), RenderTimeout);
        Assert.Contains("Term expired", page.Markup);
    }

    [Fact]
    public void AnEndedTermHeldByTheSAMEPersonAsNowIsOfferedForCollection()
    {
        // Same name on both terms: the office may legitimately collect the earlier balance from the person standing there.
        // The page tags it so the clerk verifies identity rather than assuming it.
        var page = RenderProfile("Rosa Magbanua", Term("Rosa Magbanua", 4_800m));

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll("button.prof-prior-pay")), RenderTimeout);
        Assert.Contains("Lessee of record on the current term", page.Markup);
    }

    [Theory]
    [InlineData("Rosa  Magbanua")]      // typed with a double space
    [InlineData("  Rosa Magbanua ")]    // padded, as pasted
    [InlineData("ROSA MAGBANUA")]       // all caps
    [InlineData("rosa magbanua")]       // all lower
    public void TheSamePersonSpelledDifferentlyIsStillTheSamePerson(string asTypedOnTheEarlierTerm)
    {
        // The office's rule (2026-08-16): within one LGU a name identifies the client, however it is spelled or spaced. This
        // used to be a trim-and-compare, so an internal double space made the earlier balance uncollectable from the person
        // standing in the stall — the same pair of names the slaughterhouse already treated as one client.
        var page = RenderProfile("Rosa Magbanua", Term(asTypedOnTheEarlierTerm, 4_800m));

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll("button.prof-prior-pay")), RenderTimeout);
        Assert.Contains("Lessee of record on the current term", page.Markup);
    }

    [Theory]
    [InlineData("Rosa Magbanua Jr")]    // a different person on the same family name
    [InlineData("Rosa Magbanuo")]       // one letter apart
    [InlineData("RosaMagbanua")]        // a missing space is a different string, not a spelling variant
    public void ADifferentNameIsStillADifferentPerson(string other)
    {
        // The relaxation must not become "any similar name will do": this gate decides whether the office may post money to a
        // term the present lessee does not hold.
        var page = RenderProfile("Rosa Magbanua", Term(other, 4_800m));

        page.WaitForAssertion(() => Assert.Contains(other, page.Markup), RenderTimeout);
        Assert.Empty(page.FindAll("button.prof-prior-pay"));
    }

    [Fact]
    public void ATermWithNothingOwedIsNotListedAtAll()
    {
        // The card states what is still owed. A settled earlier term is history the register keeps, not a follow-up.
        var page = RenderProfile("Ana Reyes", Term("Rosa Magbanua", 0m));

        page.WaitForAssertion(() => Assert.False(page.Instance is null), RenderTimeout);
        Assert.Empty(page.FindAll(".prof-prior"));
    }

    [Fact]
    public void TheCardIsHeadedByWhetherATermHasLapsed()
    {
        // Two different situations, two different headings: one asks the office to renew, the other only to collect.
        var lapsed = RenderProfile("Rosa Magbanua",
            Term("Rosa Magbanua", 900m, InactiveAccountState.Lapsed));
        lapsed.WaitForAssertion(() => Assert.Contains("Term Status &amp; Earlier Terms", lapsed.Markup), RenderTimeout);
    }

    [Fact]
    public void EachTermKeepsItsOwnFigures_WhenAStallHasBeenLetMoreThanOnce()
    {
        // A re-let stall carries several terms, and the whole point is that one lessee is never credited with another's
        // collections. Both rows must state their own.
        var page = RenderProfile("Ana Reyes",
            Term("Rosa Magbanua", 4_800m, contractId: FirstTermId, lifetimeCollected: 12_000m),
            Term("Pedro Santos", 1_200m, contractId: SecondTermId, lifetimeCollected: 3_000m));

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".prof-prior").Count), RenderTimeout);

        var markup = page.Markup;
        Assert.Contains("4,800", markup);
        Assert.Contains("1,200", markup);
        Assert.Contains("12,000", markup);
        Assert.Contains("3,000", markup);
    }
}
