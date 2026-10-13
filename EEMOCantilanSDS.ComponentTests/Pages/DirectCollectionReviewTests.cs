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
            // The long explanation is a tooltip on the info control, not a paragraph on the page.
            Assert.DoesNotContain(cut.FindAll("p"), p => p.TextContent.Contains("never changes the money"));
            Assert.Contains("never changes the money or the SRC", cut.Find("button[aria-label='About this review']").GetAttribute("title"));
            Assert.Equal("Direct Collection Review", cut.Find("#dcr-title").TextContent);
        }, Timeout);
    }

    private static DirectCollectionDetailDto Detail(DirectCollectionReviewRowDto row, params DirectCollectionHistoryDto[] history) =>
        new(row, "OR 1234567", "OfficialReceipt", [new("Fish / Meat Vendor Fee", row.Amount)], history);

    [Fact]
    public void ViewOpensTheSelectedCollectionsDetailInsideTheReview_NotANavigation()
    {
        var row = Row("SRC-2026-000016", 59m);
        Serve(row);
        _api.Setup(x => x.DetailAsync(row.CollectionId)).ReturnsAsync(Result<DirectCollectionDetailDto>.Success(
            Detail(row, new DirectCollectionHistoryDto("Needs correction", "Amount looks short", "head-reviewer", new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc)))));
        var cut = Open();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[aria-label='View record SRC-2026-000016']")), Timeout);

        cut.Find("button[aria-label='View record SRC-2026-000016']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("SRC-2026-000016", cut.Find("#dcr-title").TextContent);
            var detail = cut.Find(".dcr-detail").TextContent;
            Assert.Contains("₱59.00", detail); Assert.Contains("Juan Dela Cruz", detail); Assert.Contains("Pat Collector", detail);
            Assert.Contains("OR 1234567", detail); Assert.Contains("Fish / Meat Vendor Fee", detail);
            Assert.Contains("Amount looks short", detail); Assert.Contains("head-reviewer", detail);
        }, Timeout);
        _api.Verify(x => x.DetailAsync(row.CollectionId), Times.Once);
        cut.Find(".dcr-back").Click();
        cut.WaitForAssertion(() => Assert.Equal("Direct Collection Review", cut.Find("#dcr-title").TextContent), Timeout);
    }

    [Fact]
    public void AFailedDetailIsStatedWithItsOwnBackButton()
    {
        var row = Row("SRC-2026-000016");
        Serve(row);
        _api.Setup(x => x.DetailAsync(row.CollectionId)).ReturnsAsync(Result<DirectCollectionDetailDto>.Failure("Unavailable"));
        var cut = Open();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[aria-label='View record SRC-2026-000016']")), Timeout);
        cut.Find("button[aria-label='View record SRC-2026-000016']").Click();
        cut.WaitForAssertion(() => Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent), Timeout);
        Assert.NotNull(cut.Find(".dcr-back"));
    }

    [Fact]
    public void APayorWhoIsOnlyANameIsMarkedNeedsRegistration_AsAStatusSeparateFromFinancialReview()
    {
        var needs = Row("SRC-2026-000020", 43m, canConfirm: false) with { Operation = "Fish / Meat Vendor Fee", Utility = null, Payor = "Giorgia Macapagal", Registration = "Needs registration" };
        _api.Setup(x => x.GetAsync(It.IsAny<DirectCollectionReviewRequest>()))
            .ReturnsAsync(Result<DirectCollectionReviewPageDto>.Success(new([needs], 1, 1, 1)));
        var cut = Open();

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("tbody tr").TextContent;
            Assert.Contains("Awaiting review", row);          // the financial question
            Assert.Contains("Needs registration", row);       // the identity question, a separate chip
            Assert.Empty(cut.FindAll("input[type=checkbox]")); // never part of a bulk confirm
            Assert.Contains("Needs registration (1)", cut.Find(".dcr-tabs").TextContent);
            Assert.NotEmpty(cut.FindAll("button[aria-label='Confirm SRC-2026-000020']")); // a single financial confirm stays available
        }, Timeout);
        cut.FindAll(".dcr-tab").Single(t => t.TextContent.StartsWith("Needs registration")).Click();
        _api.Verify(x => x.GetAsync(It.Is<DirectCollectionReviewRequest>(r => r.State == "NeedsRegistration")), Times.AtLeastOnce);
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
