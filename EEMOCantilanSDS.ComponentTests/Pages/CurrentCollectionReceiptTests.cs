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
/// The physical Official Receipt is the office's own booklet. The collection screen suggests the next assigned receipt in its
/// registered sequence; browsing the stock is a deliberate recovery action. Opening the screen never selects or consumes one.
/// </summary>
public sealed class CurrentCollectionReceiptTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid NextId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private readonly Mock<IEcfCollectionsApiClient> _collections = new();

    public CurrentCollectionReceiptTests()
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
        _collections.Setup(x => x.GetAvailableReceiptsAsync()).ReturnsAsync(Result<IReadOnlyList<EcfAvailableDocumentDto>>.Success(
        [
            new(NextId, "2315601 A", AccountableDocumentState.Assigned, true),
            new(OtherId, "2315602 A", AccountableDocumentState.Assigned),
        ]));
    }

    private static EcfCollectionDraftDto Draft(Guid? documentId = null) => new(
        Guid.NewGuid(), 1, PhilippineTime.Today, "Draft", false, null, "Lisa Reyes", Guid.NewGuid(), documentId, null,
        500m, null, [new(Guid.NewGuid(), 500m, "Electricity Consumption Fee", Guid.NewGuid(), CollectionSourcePart.Electricity, 1, 500m,
            CollectionSourceKind.UtilityBill, Guid.NewGuid(), [], null)]);

    private IRenderedComponent<CurrentCollection> Render(EcfCollectionDraftDto draft)
    {
        _collections.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft));
        var cut = RenderComponent<CurrentCollection>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[aria-label='Official Receipt']")), Timeout);
        return cut;
    }

    [Fact]
    public void TheNextAssignedReceiptIsShown_WithoutABrowsableList_AndNothingIsSelectedByOpeningTheScreen()
    {
        var cut = Render(Draft());

        var receipt = cut.Find("[aria-label='Official Receipt']");
        Assert.Contains("2315601 A", receipt.TextContent);
        Assert.Contains("Next assigned receipt", receipt.TextContent);
        Assert.Empty(receipt.QuerySelectorAll("select"));
        Assert.DoesNotContain("2315602 A", receipt.TextContent);
        _collections.Verify(x => x.SelectDocumentAsync(It.IsAny<Guid>(), It.IsAny<SelectEcfDraftDocumentRequest>()), Times.Never);
    }

    [Fact]
    public void UsingTheNextReceipt_SelectsExactlyThatOne_OnTheCurrentRevision()
    {
        var draft = Draft();
        _collections.Setup(x => x.SelectDocumentAsync(draft.DraftId, It.IsAny<SelectEcfDraftDocumentRequest>()))
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Success(Draft(NextId) with { DraftId = draft.DraftId, Revision = 2 }));
        var cut = Render(draft);

        cut.FindAll("[aria-label='Official Receipt'] button").Single(b => b.TextContent.Trim() == "Use this receipt").Click();

        cut.WaitForAssertion(() => _collections.Verify(x => x.SelectDocumentAsync(draft.DraftId,
            It.Is<SelectEcfDraftDocumentRequest>(r => r.AccountableDocumentId == NextId && r.ExpectedRevision == 1)), Times.Once), Timeout);
        cut.WaitForAssertion(() => Assert.Contains("Next assigned receipt", cut.Find("[aria-label='Official Receipt']").TextContent), Timeout);
        Assert.Empty(cut.FindAll("[aria-label='Official Receipt'] button").Where(b => b.TextContent.Trim() == "Use this receipt"));
    }

    [Fact]
    public void ChoosingADifferentReceipt_IsASecondaryActionThatRevealsTheRecoveryList()
    {
        var draft = Draft();
        _collections.Setup(x => x.SelectDocumentAsync(draft.DraftId, It.IsAny<SelectEcfDraftDocumentRequest>()))
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Success(Draft(OtherId) with { DraftId = draft.DraftId, Revision = 2 }));
        var cut = Render(draft);

        cut.FindAll("[aria-label='Official Receipt'] button").Single(b => b.TextContent.Trim() == "Use different receipt").Click();
        var picker = cut.Find("[aria-label='Official Receipt'] select");
        Assert.Contains("2315602 A", picker.InnerHtml);
        picker.Change(OtherId.ToString());

        cut.WaitForAssertion(() => _collections.Verify(x => x.SelectDocumentAsync(draft.DraftId,
            It.Is<SelectEcfDraftDocumentRequest>(r => r.AccountableDocumentId == OtherId)), Times.Once), Timeout);
        cut.WaitForAssertion(() => Assert.Contains("Different receipt selected", cut.Find("[aria-label='Official Receipt']").TextContent), Timeout);
    }

    [Fact]
    public void WithNoReceiptAvailable_TheOfficeIsToldToRegisterOrAssignOne()
    {
        _collections.Setup(x => x.GetAvailableReceiptsAsync())
            .ReturnsAsync(Result<IReadOnlyList<EcfAvailableDocumentDto>>.Success([]));
        var cut = Render(Draft());

        Assert.Contains("Ask the office to register or assign one", cut.Find("[aria-label='Official Receipt']").TextContent);
        Assert.Empty(cut.FindAll("[aria-label='Official Receipt'] button"));
    }
}
