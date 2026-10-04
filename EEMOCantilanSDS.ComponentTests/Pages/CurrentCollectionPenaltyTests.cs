using Bunit;
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
/// An approved penalty can be added to an Official Receipt draft only from an approved definition, only once the draft
/// has a rent or ECF item, and never as a typed charge: a fixed penalty is displayed, not entered. (IA-049)
/// </summary>
public sealed class CurrentCollectionPenaltyTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IEcfCollectionsApiClient> _collections = new();
    private readonly Mock<IPenaltiesApiClient> _penalties = new();

    public CurrentCollectionPenaltyTests()
    {
        Services.AddSingleton(_collections.Object);
        Services.AddSingleton(_penalties.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _collections.Setup(x => x.GetCollectionActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success([]));
    }

    private static EcfCollectionDraftLineDto EcfLine(decimal amount = 500m) => new(
        Guid.NewGuid(), amount, "Electricity Consumption Fee", Guid.NewGuid(), CollectionSourcePart.Electricity, 1, amount,
        CollectionSourceKind.UtilityBill, Guid.NewGuid(), [], null);

    private static EcfCollectionDraftLineDto PenaltyLine(decimal amount = 50m) => new(
        Guid.NewGuid(), amount, "Late payment penalty", Guid.Empty, CollectionSourcePart.Electricity, 0, 0m,
        CollectionSourceKind.PenaltyDefinition, Guid.NewGuid(), [], null);

    private static EcfCollectionDraftDto Draft(long revision, params EcfCollectionDraftLineDto[] lines) => new(
        Guid.NewGuid(), revision, PhilippineTime.Today, "Draft", false, null, "Lisa Reyes", Guid.NewGuid(), null, null,
        lines.Sum(x => x.Amount), null, lines);

    private static PenaltyDefinitionDto Fixed(decimal amount = 50m) => new(
        Guid.NewGuid(), "LATE_PAYMENT", "Late payment penalty", "Stall rent arrears", GovernedServiceBasis.FixedAmount,
        amount, null, true, new DateOnly(2026, 1, 1));

    private void Serve(EcfCollectionDraftDto? draft, params PenaltyDefinitionDto[] penalties)
    {
        _collections.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(draft is null
            ? Result<EcfCollectionDraftDto>.NotFound() : Result<EcfCollectionDraftDto>.Success(draft));
        _penalties.Setup(x => x.GetDefinitionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success(penalties));
    }

    [Fact]
    public void PenaltyControl_IsOffered_OnlyWhenTheDraftHasAnAllocatedItem_AndAnApprovedPenaltyExists()
    {
        Serve(Draft(1, EcfLine()), Fixed());
        var withItem = RenderComponent<CurrentCollection>();
        withItem.WaitForAssertion(() => Assert.NotEmpty(withItem.FindAll("[aria-label='Add an approved penalty']")), Timeout);

        Serve(Draft(1, EcfLine()));   // no approved penalty defined
        var noDefinition = RenderComponent<CurrentCollection>();
        noDefinition.WaitForAssertion(() => Assert.Contains("Review Collection", noDefinition.Markup), Timeout);
        Assert.Empty(noDefinition.FindAll("[aria-label='Add an approved penalty']"));

        Serve(null, Fixed());          // no draft yet
        var noDraft = RenderComponent<CurrentCollection>();
        noDraft.WaitForAssertion(() => Assert.Contains("Add an eligible operation item", noDraft.Markup), Timeout);
        Assert.Empty(noDraft.FindAll("[aria-label='Add an approved penalty']"));
    }

    [Fact]
    public void AFixedPenaltyIsShownNotTyped_AndAddsWithTheApprovedAmountAndCurrentRevision()
    {
        Serve(Draft(3, EcfLine()), Fixed(50m));
        AddPenaltyDraftLineRequest? sent = null;
        _collections.Setup(x => x.AddPenaltyLineAsync(It.IsAny<AddPenaltyDraftLineRequest>()))
            .Callback<AddPenaltyDraftLineRequest>(r => sent = r)
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Success(Draft(4, EcfLine(), PenaltyLine())));

        var cut = RenderComponent<CurrentCollection>();
        cut.WaitForAssertion(() => cut.Find("[aria-label='Add an approved penalty'] select"), Timeout);
        cut.Find("[aria-label='Add an approved penalty'] select").Change("LATE_PAYMENT");

        var amount = cut.Find("[aria-label='Add an approved penalty'] input[readonly]");
        Assert.Equal("PHP 50.00", amount.GetAttribute("value"));
        Assert.Empty(cut.FindAll("[aria-label='Add an approved penalty'] input[type='number']"));
        cut.Find("[aria-label='Add an approved penalty'] input[placeholder]").Input("Stall 4 · August rent");
        cut.FindAll("[aria-label='Add an approved penalty'] button").Single(b => b.TextContent.Trim() == "Add penalty").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(("LATE_PAYMENT", 50m, "Stall 4 · August rent", 3L),
                (sent!.PenaltyCode, sent.ProposedAmount, sent.Origin, sent.ExpectedRevision));
            Assert.Contains("Penalty added to this collection", cut.Markup);
            Assert.Contains("Late payment penalty", cut.Markup);
            Assert.Contains("PHP 550.00", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void ARefusedPenaltyShowsTheServersReason_AndTheDraftIsUnchanged()
    {
        Serve(Draft(1, EcfLine()), Fixed());
        _collections.Setup(x => x.AddPenaltyLineAsync(It.IsAny<AddPenaltyDraftLineRequest>()))
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Failure("This penalty is already on the draft.", ResultStatus.Conflict));

        var cut = RenderComponent<CurrentCollection>();
        cut.WaitForAssertion(() => cut.Find("[aria-label='Add an approved penalty'] select"), Timeout);
        cut.Find("[aria-label='Add an approved penalty'] select").Change("LATE_PAYMENT");
        cut.FindAll("[aria-label='Add an approved penalty'] button").Single(b => b.TextContent.Trim() == "Add penalty").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("already on the draft", cut.Find("[role='alert']").TextContent);
            Assert.Contains("PHP 500.00", cut.Markup);
            Assert.DoesNotContain("Penalty added", cut.Markup);
        }, Timeout);
    }
}
