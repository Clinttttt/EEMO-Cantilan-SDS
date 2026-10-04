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
/// IA-062: StallTrack's own reference code (SRC) is the identity of a collection. Current Collection never asks for, suggests or
/// selects a physical Official Receipt; a reviewed draft posts on its own and the success text names the SRC the server assigned.
/// </summary>
public sealed class CurrentCollectionReferenceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IEcfCollectionsApiClient> _collections = new();

    public CurrentCollectionReferenceTests()
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
        _collections.Setup(x => x.GetCollectionActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success([]));
    }

    private static EcfCollectionDraftDto Draft(bool reviewed) => new(
        Guid.NewGuid(), 1, PhilippineTime.Today, "Draft", reviewed, null, "Lisa Reyes", Guid.NewGuid(), null, null,
        500m, null, [new(Guid.NewGuid(), 500m, "Electricity Consumption Fee", Guid.NewGuid(), CollectionSourcePart.Electricity, 1, 500m,
            CollectionSourceKind.UtilityBill, Guid.NewGuid(), [], null)]);

    private IRenderedComponent<CurrentCollection> Render(EcfCollectionDraftDto draft)
    {
        _collections.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft));
        var cut = RenderComponent<CurrentCollection>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cc-total")), Timeout);
        return cut;
    }

    [Fact]
    public void ThereIsNoOfficialReceiptSelector_NextReceiptSuggestion_OrCancelAction()
    {
        var cut = Render(Draft(reviewed: true));

        Assert.Empty(cut.FindAll("[aria-label='Official Receipt']"));
        Assert.Empty(cut.FindAll("select"));
        Assert.DoesNotContain("Next assigned receipt", cut.Markup);
        Assert.DoesNotContain("Use this receipt", cut.Markup);
        Assert.DoesNotContain("Cancel receipt", cut.Markup);
        Assert.DoesNotContain("Choose an available OR", cut.Markup);
    }

    [Fact]
    public void AReviewedDraftPostsWithNoReceipt_AndTheSuccessMessageNamesTheSrc()
    {
        var draft = Draft(reviewed: true);
        _collections.Setup(x => x.PostAsync(draft.DraftId, It.IsAny<PostEcfCollectionDraftRequest>()))
            .ReturnsAsync(Result<EcfPostOutcomeDto>.Success(new(Guid.NewGuid(), "SRC-2026-000127", "Posted", 500m, 1, false)));
        var cut = Render(draft);

        var post = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Post Collection");
        Assert.False(post.HasAttribute("disabled"));
        post.Click();

        cut.WaitForAssertion(() => Assert.Contains("SRC-2026-000127", cut.Find(".cc-notice").TextContent), Timeout);
        Assert.Contains("Collection recorded", cut.Find(".cc-notice").TextContent);
        _collections.Verify(x => x.PostAsync(draft.DraftId, It.Is<PostEcfCollectionDraftRequest>(r => r.ExpectedRevision == 1)), Times.Once);
    }

    [Fact]
    public void AnUnreviewedDraftCannotBePosted_ForTheReviewReasonAloneNotForALackOfAReceipt()
    {
        var cut = Render(Draft(reviewed: false));

        var post = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Post Collection");
        Assert.True(post.HasAttribute("disabled"));
    }

    [Fact]
    public void PostedActivityIsListedByItsSrc()
    {
        _collections.Setup(x => x.GetCollectionActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success(
            [
                new(Guid.NewGuid(), PhilippineTime.Today, DateTime.UtcNow, "SRC-2026-000126", "Lisa Reyes", 300m, 1, "Posted", [])
            ]));
        var cut = Render(Draft(reviewed: false));

        cut.WaitForAssertion(() => Assert.Contains("SRC-2026-000126", cut.Find(".cc-activity").TextContent), Timeout);
        Assert.Contains("Reference / payer", cut.Find(".cc-activity").TextContent);
    }
}
