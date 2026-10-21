using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Recording an ECF payment on the Web goes through the existing server draft: add the source with the amount actually received,
/// review that exact revision, then record under a stable operation id. It never types a receipt number and never treats the
/// approved amount as money received.
/// </summary>
public sealed class EcfRecordPaymentDialogTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid BillId = Guid.NewGuid();

    public EcfRecordPaymentDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    [Fact]
    public void Record_AddsReviewsAndPosts_TheTypedAmount_AndShowsTheServerIssuedSrc()
    {
        var api = Api();
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<EcfRecordPaymentDialog>(p => p.Add(x => x.Quote, Quote(outstanding: 80m)));

        Assert.Equal("80.00", cut.Find("input").GetAttribute("value"));                       // suggested, never assumed received
        Assert.DoesNotContain("receipt number", cut.Markup, StringComparison.OrdinalIgnoreCase);
        cut.Find("input").Input("50.25");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review collection").Click();
        api.Verify(x => x.AddEcfLineAsync(It.Is<AddEcfDraftLineRequest>(r => r.UtilityBillId == BillId && r.ProposedAmount == 50.25m)), Times.Once);
        api.Verify(x => x.ReviewAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>()), Times.Once);
        api.Verify(x => x.PostAsync(It.IsAny<Guid>(), It.IsAny<PostEcfCollectionDraftRequest>()), Times.Never);

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Record collection"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Record collection").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("SRC-0001", cut.Find("dl").TextContent);
            Assert.Contains("₱50.25", cut.Find("dl").TextContent);
        }, Timeout);
        api.Verify(x => x.PostAsync(It.IsAny<Guid>(), It.Is<PostEcfCollectionDraftRequest>(r => r.ClientOperationId != Guid.Empty)), Times.Once);
    }

    [Fact]
    public void Record_RejectsAnAmountThatIsNotPositivePesosAndCentavos()
    {
        Services.AddSingleton(Api().Object);
        var cut = RenderComponent<EcfRecordPaymentDialog>(p => p.Add(x => x.Quote, Quote(outstanding: 80m)));

        cut.Find("input").Input("12.345");

        Assert.Contains("positive amount", cut.Find("[role=alert]").TextContent);
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review collection").HasAttribute("disabled"));
    }

    [Fact]
    public void ResumeDraftFromAnEarlierDate_MustBeRefreshedBeforeItCanBeReviewed()
    {
        var api = Api();
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<EcfRecordPaymentDialog>(p => p.Add(x => x.Draft, Draft(refresh: true)));

        Assert.Contains("earlier business date", cut.Find("[role=alert]").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() is "Review collection" or "Record collection");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Refresh for today").Click();
        api.Verify(x => x.ResumeDraftAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>()), Times.Once);
    }

    [Fact]
    public void Discard_AsksFirst_AndCreatesNoFinancialRecord()
    {
        var api = Api();
        Services.AddSingleton(api.Object);
        var closed = false;

        var cut = RenderComponent<EcfRecordPaymentDialog>(p => p
            .Add(x => x.Draft, Draft(refresh: false)).Add(x => x.StartDiscarding, true)
            .Add(x => x.OnClosed, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<bool>(this, _ => closed = true)));

        Assert.Contains("No collection, SRC or document has been created", cut.Markup);
        api.Verify(x => x.DiscardAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>()), Times.Never);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Discard collection").Click();

        cut.WaitForAssertion(() => Assert.True(closed), Timeout);
        api.Verify(x => x.DiscardAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>()), Times.Once);
    }

    private static Mock<IEcfCollectionsApiClient> Api()
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        var reviewed = Draft(false) with { IsReviewedForCurrentRevision = true, TotalAmount = 50.25m };
        api.Setup(x => x.AddEcfLineAsync(It.IsAny<AddEcfDraftLineRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(Draft(false) with { TotalAmount = 50.25m }));
        api.Setup(x => x.ReviewAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(reviewed));
        api.Setup(x => x.ResumeDraftAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(Draft(false)));
        api.Setup(x => x.PostAsync(It.IsAny<Guid>(), It.IsAny<PostEcfCollectionDraftRequest>()))
            .ReturnsAsync(Result<EcfPostOutcomeDto>.Success(new EcfPostOutcomeDto(Guid.NewGuid(), "SRC-0001", "Posted", 50.25m, 1, false)));
        api.Setup(x => x.DiscardAsync(It.IsAny<Guid>(), It.IsAny<EcfDraftRevisionRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(Draft(false)));
        return api;
    }

    private static EcfCollectionDraftDto Draft(bool refresh) => new(
        Guid.NewGuid(), 1, new DateOnly(2026, 10, 1), "Open", false, null, "Juan Dela Cruz", null, null, null, 40m, null, [], refresh);

    private static EcfObligationQuoteDto Quote(decimal outstanding) => new(
        Guid.NewGuid(), BillId, CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 1,
        Guid.NewGuid(), "12", "Tenant Market", "Dry Goods", 2026, 9, 100m, 140m, 40m, 2m,
        outstanding, 0m, outstanding, SettlementAuthority.Canonical, null, "Juan Dela Cruz",
        Guid.NewGuid(), Guid.NewGuid(), "ECF", RevenueInstrumentType.OfficialReceipt, "Metered", true, true);
}
