using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Current Collection is payer-first. A search lists explicit Business Payors as direct rows, selecting one lists what that
/// Payor can pay, and an operation that already knows the Payor opens the collection on that Payor's id — never on a name.
/// </summary>
public sealed class CurrentCollectionDiscoveryTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid PayorId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private readonly Mock<IEcfCollectionsApiClient> _collections = new();

    public CurrentCollectionDiscoveryTests()
    {
        Services.AddSingleton(_collections.Object);
        Services.AddSingleton(Mock.Of<IPenaltiesApiClient>(x => x.GetDefinitionsAsync() ==
            Task.FromResult(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success(Array.Empty<PenaltyDefinitionDto>()))));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _collections.Setup(x => x.GetCollectionActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success([]));
        _collections.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(Result<EcfCollectionDraftDto>.NotFound());
    }

    private static CollectionCandidateDto SpaceCandidate() => new(
        CollectionSourceKind.ObligationPeriod, AccountId, null, Guid.Empty, "Space K-9",
        "Kanmanggay Space Rental · Space K-9", 2026, 10, PayorId, "Lisa Ilogans", 900m,
        RevenueInstrumentType.OfficialReceipt, true);

    private void ServeCandidates(params CollectionCandidateDto[] candidates) =>
        _collections.Setup(x => x.GetPayorObligationsAsync(PayorId))
            .ReturnsAsync(Result<IReadOnlyList<CollectionCandidateDto>>.Success(candidates));

    [Fact]
    public void ASearchListsPayorsAsRows_AndSelectingOneShowsWhatItCanPay()
    {
        _collections.Setup(x => x.SearchCollectionPayorsAsync("lisa")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success([new CollectionPayorDto(PayorId, "Lisa Ilogans")]));
        ServeCandidates(SpaceCandidate());

        var cut = RenderComponent<CurrentCollection>();
        cut.WaitForAssertion(() => Assert.Contains("Search for a payer.", cut.Markup), Timeout);
        cut.Find("#cc-payor-search").Input("lisa");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Search").Click();

        cut.WaitForAssertion(() => Assert.Equal("Lisa Ilogans", Assert.Single(cut.FindAll(".cc-result")).TextContent.Trim()), Timeout);
        Assert.Empty(cut.FindAll("select"));
        cut.Find(".cc-result").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Kanmanggay Space Rental · Space K-9", cut.Find(".cc-table").TextContent);
            Assert.Contains("₱900.00", cut.Find(".cc-table").TextContent);
        }, Timeout);
        _collections.Verify(x => x.GetPayorObligationsAsync(PayorId), Times.Once);
    }

    [Fact]
    public void ASearchWithNoMatch_SaysSoInOneLine()
    {
        _collections.Setup(x => x.SearchCollectionPayorsAsync("zzz")).ReturnsAsync(
            Result<IReadOnlyList<CollectionPayorDto>>.Success([]));

        var cut = RenderComponent<CurrentCollection>();
        cut.WaitForAssertion(() => cut.Find("#cc-payor-search"), Timeout);
        cut.Find("#cc-payor-search").Input("zzz");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Search").Click();

        cut.WaitForAssertion(() => Assert.Contains("No payer found.", cut.Markup), Timeout);
    }

    [Fact]
    public void AnObligationChargeIsAddedThroughTheComposer_ByItsAccountId()
    {
        ServeCandidates(SpaceCandidate());
        AddObligationDraftAllocationRequest? sent = null;
        _collections.Setup(x => x.AddObligationAllocationAsync(It.IsAny<AddObligationDraftAllocationRequest>()))
            .Callback<AddObligationDraftAllocationRequest>(r => sent = r)
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Success(new(
                Guid.NewGuid(), 1, PhilippineTime.Today, "Draft", false, null, "Lisa Ilogans", PayorId, null, null, 900m, null,
                [new(Guid.NewGuid(), 900m, "Kanmanggay Space Rental", Guid.NewGuid(), CollectionSourcePart.DailyFee, 1, 900m,
                    CollectionSourceKind.ObligationPeriod, AccountId,
                    [new(Guid.NewGuid(), CollectionSourceKind.ObligationPeriod, Guid.NewGuid(), null, 900m, 2026, 10,
                        "Kanmanggay Space Rental · Space K-9", SettlementAuthority.Canonical)], null)])));

        Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>()
            .NavigateTo($"/collections/current?payor={PayorId}&name=Lisa%20Ilogans");
        var cut = RenderComponent<CurrentCollection>();

        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Space Rental · Space K-9", cut.Markup), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((AccountId, 2026, 10, 900m), (sent!.AccountId, sent.BillingYear, sent.BillingMonth, sent.ProposedAmount));
            Assert.Contains("₱900.00", cut.Find(".cc-total").TextContent);
            // What is already in the review is no longer offered again: the row can't be added twice past its balance.
            Assert.True(cut.Find(".cc-table tbody tr button").HasAttribute("disabled"));
        }, Timeout);
    }

    [Fact]
    public void ArrivingWithAKnownPayor_OpensItsChargesWithoutASearch()
    {
        ServeCandidates(SpaceCandidate());
        Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>()
            .NavigateTo($"/collections/current?payor={PayorId}&name=Lisa%20Ilogans");

        var cut = RenderComponent<CurrentCollection>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Lisa Ilogans", cut.Markup);
            Assert.Contains("Kanmanggay Space Rental · Space K-9", cut.Find(".cc-table").TextContent);
        }, Timeout);
        _collections.Verify(x => x.SearchCollectionPayorsAsync(It.IsAny<string>()), Times.Never);
    }
}
