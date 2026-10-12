using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Head's direct-collection review: a compact control with a count, a panel over canonical Collections, and administrative actions that
/// say they never change the money. A flag needs a note, a possible duplicate is not bulk-confirmable, and failures are stated with a retry.
/// </summary>
public sealed class DirectCollectionReviewTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IDirectCollectionReviewApiClient> _api = new();

    public DirectCollectionReviewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    private static DirectCollectionReviewRowDto Row(string src, decimal amount = 656m, string state = "Awaiting review", bool duplicate = false, bool canConfirm = true) =>
        new(Guid.NewGuid(), src, new DateOnly(2026, 10, 12), new DateTime(2026, 10, 12, 10, 14, 0, DateTimeKind.Utc), "Electricity Consumption Fees", "ECF",
            "Juan Dela Cruz", "Public Market · Stall V-01", amount, "Pat Collector", "Direct", "Source verified", "Posted", state, null, null, null, duplicate, canConfirm);

    private void Serve(params DirectCollectionReviewRowDto[] rows) => _api
        .Setup(x => x.GetAsync(It.IsAny<DirectCollectionReviewRequest>()))
        .ReturnsAsync(Result<DirectCollectionReviewPageDto>.Success(new(rows, rows.Length, rows.Count(r => r.ReviewState == "Awaiting review"))));

    private IRenderedComponent<DirectCollectionReview> Open()
    {
        var cut = RenderComponent<DirectCollectionReview>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".dcr-trigger")), Timeout);
        cut.Find(".dcr-trigger").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role=dialog]")), Timeout);
        return cut;
    }

    [Fact]
    public void TheControlIsACompactIconWithAnAccessibleNameAndThePendingCount()
    {
        Serve(Row("SRC-2026-000016"), Row("SRC-2026-000017"));

        var cut = RenderComponent<DirectCollectionReview>();

        cut.WaitForAssertion(() =>
        {
            var trigger = cut.Find(".dcr-trigger");
            Assert.Equal("Review direct collections, 2 awaiting review", trigger.GetAttribute("aria-label"));
            Assert.Equal("Review direct collections", trigger.GetAttribute("title"));
            Assert.Equal("2", cut.Find(".dcr-badge").TextContent.Trim());
        }, Timeout);
    }

    [Fact]
    public void ThePanelListsDirectCollectionsWithTheirSrcAndStatesThatReviewNeverMovesMoney()
    {
        Serve(Row("SRC-2026-000016"));

        var cut = Open();

        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[role=dialog]");
            Assert.Contains("SRC-2026-000016", dialog.TextContent);
            Assert.Contains("₱656.00", dialog.TextContent);
            Assert.Contains("Pat Collector", dialog.TextContent);
            Assert.Contains("Awaiting review", dialog.TextContent);
            Assert.Contains("never changes the money or the SRC", dialog.TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/collections/activity?src=SRC-2026-000016");
        }, Timeout);
    }

    [Fact]
    public void ConfirmingSendsOneReviewForThatCollection_AndSaysNoCollectionWasChanged()
    {
        var row = Row("SRC-2026-000016");
        Serve(row);
        DirectCollectionReviewActionRequest? sent = null;
        _api.Setup(x => x.ReviewAsync(It.IsAny<DirectCollectionReviewActionRequest>()))
            .Callback<DirectCollectionReviewActionRequest>(r => sent = r)
            .ReturnsAsync(Result<DirectCollectionReviewRowDto>.Success(row with { ReviewState = "Confirmed" }));
        var cut = Open();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[aria-label='Confirm SRC-2026-000016']")), Timeout);

        cut.Find("button[aria-label='Confirm SRC-2026-000016']").Click();

        cut.WaitForAssertion(() => Assert.Contains("No collection was changed", cut.Find("p.v3-notice").TextContent), Timeout);
        Assert.Equal((row.CollectionId, "Confirmed"), (sent!.CollectionId, sent.Outcome));
        _api.Verify(x => x.ReviewAsync(It.IsAny<DirectCollectionReviewActionRequest>()), Times.Once);
    }

    [Fact]
    public void FlaggingNeedsANote_AndSendsItWithTheFlag()
    {
        var row = Row("SRC-2026-000016");
        Serve(row);
        DirectCollectionReviewActionRequest? sent = null;
        _api.Setup(x => x.ReviewAsync(It.IsAny<DirectCollectionReviewActionRequest>()))
            .Callback<DirectCollectionReviewActionRequest>(r => sent = r)
            .ReturnsAsync(Result<DirectCollectionReviewRowDto>.Success(row with { ReviewState = "Needs correction" }));
        var cut = Open();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[aria-label='Flag SRC-2026-000016 for correction']")), Timeout);

        cut.Find("button[aria-label='Flag SRC-2026-000016 for correction']").Click();
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Flag").HasAttribute("disabled"));     // no note, no flag

        cut.Find("textarea").Input("Amount looks short");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Flag").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal(("NeedsCorrection", "Amount looks short"), (sent!.Outcome, sent.Note));
    }

    [Fact]
    public void APossibleDuplicateIsMarkedAndCannotBeSelectedForBulkConfirmation()
    {
        Serve(Row("SRC-2026-000016", duplicate: true, canConfirm: false), Row("SRC-2026-000017"));

        var cut = Open();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Possible duplicate", cut.Find("[role=dialog]").TextContent);
            var boxes = cut.FindAll("input[type=checkbox]");
            Assert.Single(boxes);
            Assert.Equal("Select SRC-2026-000017", boxes[0].GetAttribute("aria-label"));
        }, Timeout);
    }

    [Fact]
    public void SelectedCollectionsAreConfirmedInOneCall_AndSkippedOnesAreReported()
    {
        var a = Row("SRC-2026-000016"); var b = Row("SRC-2026-000017");
        Serve(a, b);
        DirectCollectionBulkConfirmRequest? sent = null;
        _api.Setup(x => x.ConfirmManyAsync(It.IsAny<DirectCollectionBulkConfirmRequest>()))
            .Callback<DirectCollectionBulkConfirmRequest>(r => sent = r)
            .ReturnsAsync(Result<DirectCollectionBulkConfirmResult>.Success(new(1, [new(b.CollectionId, "Already reviewed.")])));
        var cut = Open();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("input[type=checkbox]").Count), Timeout);

        cut.FindAll("input[type=checkbox]")[0].Change(true);
        cut.FindAll("input[type=checkbox]")[1].Change(true);
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Confirm selected").Click();

        cut.WaitForAssertion(() => Assert.Contains("1 confirmed, 1 left for individual review", cut.Find("p.v3-notice").TextContent), Timeout);
        Assert.Equal(2, sent!.CollectionIds.Count);
    }

    [Fact]
    public void ALoadFailureIsStatedWithARetry_NeverAsAnEmptyQueue()
    {
        _api.Setup(x => x.GetAsync(It.IsAny<DirectCollectionReviewRequest>()))
            .ReturnsAsync(Result<DirectCollectionReviewPageDto>.Failure("Unavailable"));

        var cut = Open();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[role=alert]");
            Assert.Contains("Unavailable", alert.TextContent);
            Assert.Contains("Try again", alert.TextContent);
            Assert.DoesNotContain("No direct collections are waiting", cut.Markup);
        }, Timeout);
    }
}
