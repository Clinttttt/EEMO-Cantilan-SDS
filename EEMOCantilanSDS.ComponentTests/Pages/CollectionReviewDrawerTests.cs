using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TransactionsPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Transactions;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The per-SRC review drawer on Collection Activity: facts from the loaded row, eligibility from the server, a Head-only reversal.</summary>
public sealed class CollectionReviewDrawerTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid CollectionId = Guid.NewGuid();
    private readonly Mock<ICollectionActivityApiClient> _activity = new();

    private static CollectionActivityEventDto Canonical() => new("c1", "Canonical", "Collection", CollectionId, PhilippineTime.Today,
        DateTime.UtcNow, "SRC-2026-000003", null, null, [], [], "Synthetic Occupant", null, null, null, "head", FacilityCode.NCC, "PREVIEW-NCC-02",
        null, 600m, 0m, 600m, "Posted", null,
        [new CollectionActivityLineDto("PERMANENT_STALL_RENT", "Permanent Stall Rent", 600m, "PaymentRecord", null, Guid.NewGuid(), 2026, 10, "NCC rent")], []);

    private static CollectionActivityEventDto Legacy() => new("l1", "Legacy", "PaymentRecord", null, PhilippineTime.Today,
        DateTime.UtcNow, null, "546564", RevenueInstrumentType.OfficialReceipt, [], [], "Legacy Payor", null, null, null, "office", FacilityCode.NCC, "1",
        "Paid", 1200m, 0m, 1200m, "Recorded", null,
        [new CollectionActivityLineDto("PERMANENT_STALL_RENT", "Permanent Stall Rent", 1200m, null, null, null, null, null, null)], []);

    private static CollectionReviewDto Review(bool eligible, string? code = null, string text = "Eligible: the Head may reverse this receipt today.", bool remitted = false) =>
        new(CollectionId, "SRC-2026-000003", PhilippineTime.Today, remitted, false, new(eligible, code, text));

    private IRenderedComponent<TransactionsPage> Render(CollectionReviewDto? review, params CollectionActivityEventDto[] events)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _activity.Setup(c => c.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync((DateOnly from, DateOnly to, FacilityCode? _, Guid? _, string? _, int _, string? _) =>
                Result<CollectionActivityFeedDto>.Success(new(from, to, "basis", "LatestCorrected", DateTime.UtcNow, events, events.Length, false, 0m, 0m, 0m, 0m, [])));
        if (review is not null)
            _activity.Setup(c => c.GetReviewAsync(CollectionId)).ReturnsAsync(Result<CollectionReviewDto>.Success(review));
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(c => c.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(Array.Empty<FacilitySidebarSummaryDto>()));
        var direct = new Mock<IDirectCollectionReviewApiClient>();
        direct.Setup(x => x.GetAsync(It.IsAny<EEMOCantilanSDS.Application.Dtos.Revenue.DirectCollectionReviewRequest>()))
            .ReturnsAsync(Result<EEMOCantilanSDS.Application.Dtos.Revenue.DirectCollectionReviewPageDto>.Success(new([], 0, 2)));
        Services.AddSingleton(_activity.Object);
        Services.AddSingleton(direct.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IOfficeSourcesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var page = RenderComponent<TransactionsPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".txn-row")), Timeout);
        return page;
    }

    [Fact]
    public void Review_shows_the_selected_SRC_facts_eligibility_and_a_reconciliation_link()
    {
        var page = Render(Review(true), Canonical());
        page.Find(".txn-row").Click();

        page.WaitForAssertion(() => Assert.Contains("Eligible", page.Find(".txn-note").TextContent), Timeout);
        var drawer = page.Find(".txr").TextContent;
        Assert.Contains("SRC-2026-000003", drawer); Assert.Contains("Oct 2026", drawer); Assert.Contains("600.00", drawer);
        Assert.Contains("None recorded", drawer);                        // no physical OR/CT is invented
        Assert.Contains("Not remitted", drawer);
        var link = page.Find(".txr-foot a").GetAttribute("href");
        Assert.Equal($"/reports/collection-reconciliation?from={PhilippineTime.Today:yyyy-MM-dd}&to={PhilippineTime.Today:yyyy-MM-dd}", link);
        _activity.Verify(c => c.GetReviewAsync(CollectionId), Times.Once);
    }

    [Fact]
    public void Collection_activity_review_needs_no_duplicate_review_details_button()
    {
        var eventWithVerification = Canonical() with { Verification = "Awaiting verification" };
        var page = Render(Review(true), eventWithVerification);
        page.Find(".txn-row").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".txr-foot")), Timeout);
        Assert.DoesNotContain("Review details", page.Find(".txr-foot").TextContent);
        Assert.Contains("View Reconciliation", page.Find(".txr-foot").TextContent);
    }

    [Fact]
    public void An_Office_origin_receipt_is_not_called_remitted_or_deposited()
    {
        var page = Render(new CollectionReviewDto(CollectionId, "SRC-2026-000003", PhilippineTime.Today, false, false,
            new(true, null, "Eligible: the Head may reverse this receipt today."), true), Canonical());
        page.Find(".txn-row").Click();

        page.WaitForAssertion(() => Assert.Contains("Office-origin receipt", page.Find(".txn-note").TextContent), Timeout);
        var note = page.Find(".txn-note").TextContent;
        Assert.Contains("not established", note); Assert.DoesNotContain("Not remitted", note); Assert.DoesNotContain("deposit", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_turned_over_Office_receipt_says_so_and_does_not_claim_the_status_is_unknown()
    {
        var page = Render(new CollectionReviewDto(CollectionId, "SRC-2026-000003", PhilippineTime.Today, false, false,
            new(false, "TurnedOver", "This Office cash is covered by an Office cash turnover. Void that turnover first if it was recorded in error."), true), Canonical());
        page.Find(".txn-row").Click();

        page.WaitForAssertion(() => Assert.Contains("covered by an Office cash turnover", page.Find(".txn-note").TextContent), Timeout);
        var note = page.Find(".txn-note").TextContent;
        Assert.DoesNotContain("not established", note); Assert.DoesNotContain("deposit", note, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(page.FindAll(".txn-reason"));
    }

    [Fact]
    public void Direct_collections_show_their_verification_and_a_separate_verified_figure_while_other_sources_show_none()
    {
        var verified = Canonical() with { EventKey = "d1", Verification = "Verified" };
        var pending = Canonical() with { EventKey = "d2", CollectionId = Guid.NewGuid(), ReferenceCode = "SRC-2026-000004", Amount = 400m, NetAmount = 400m, Verification = "Awaiting verification" };
        var office = Canonical() with { EventKey = "o1", CollectionId = Guid.NewGuid(), ReferenceCode = "SRC-2026-000005", Amount = 50m, NetAmount = 50m, Verification = null };
        var page = Render(null, verified, pending, office);

        var statuses = page.FindAll(".txn-verify").Select(x => x.TextContent.Trim()).ToArray();
        Assert.Equal(["Verified", "Awaiting verification"], statuses);                        // the Office receipt carries no label
        var figure = page.FindAll(".v3h-figure").Single(f => f.TextContent.Contains("Direct, verified")).TextContent;
        Assert.Contains("600.00", figure);                                                    // verified direct cash
        Assert.Contains("1,000.00 recorded", figure.Replace("₱", ""));                        // of the direct cash recorded (600 + 400)
        Assert.Contains("1,050.00", page.FindAll(".v3h-figure").Single(f => f.TextContent.StartsWith("Collected")).TextContent.Replace("₱", ""));  // the actual-cash figure is untouched by verification
    }

    [Fact]
    public void An_ineligible_receipt_explains_why_and_offers_no_reversal()
    {
        var page = Render(Review(false, "Remitted", "This cash is already part of a remittance. It cannot be reversed here.", remitted: true), Canonical());
        page.Find(".txn-row").Click();

        page.WaitForAssertion(() => Assert.Contains("remittance", page.Find(".txn-note").TextContent), Timeout);
        Assert.Contains("Remitted.", page.Find(".txn-note").TextContent);
        Assert.Empty(page.FindAll(".txn-reason"));
        Assert.DoesNotContain("Reverse collection", page.Find(".txr").TextContent);
    }

    [Fact]
    public void A_legacy_payment_shows_its_history_with_no_SRC_review_call_and_no_correction()
    {
        var page = Render(null, Legacy());
        page.Find(".txn-row").Click();

        page.WaitForAssertion(() => Assert.Contains("legacy record", page.Find(".txr").TextContent), Timeout);
        Assert.Contains("546564", page.Find(".txr").TextContent);
        Assert.Contains("Not issued (legacy record)", page.Find(".txr-meta").TextContent);
        Assert.Contains("recorded through a legacy financial workflow", page.Find(".txn-note").TextContent);
        Assert.DoesNotContain("recorded before SRCs", page.Find(".txr").TextContent);
        Assert.Empty(page.FindAll(".txn-reason"));
        _activity.Verify(c => c.GetReviewAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void After_a_reversal_the_drawer_shows_Reversed_and_the_correction_and_no_reversal_controls()
    {
        var corrected = Canonical() with { CorrectionEffect = -600m, NetAmount = 0m, Corrections = [new(Guid.NewGuid(), "Reversal", PhilippineTime.Today, DateTime.UtcNow, -600m, null, "Wrong month")] };
        var page = Render(Review(true), Canonical());
        // The server now reports the correction; the feed reload returns the corrected event.
        _activity.SetupSequence(c => c.GetReviewAsync(CollectionId))
            .ReturnsAsync(Result<CollectionReviewDto>.Success(Review(true)))
            .ReturnsAsync(Result<CollectionReviewDto>.Success(new(CollectionId, "SRC-2026-000003", PhilippineTime.Today, false, true,
                new(false, "AlreadyCorrected", "A correction is already recorded for this collection. A collection is corrected once; the original SRC is kept."))));
        _activity.Setup(c => c.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync((DateOnly from, DateOnly to, FacilityCode? _, Guid? _, string? _, int _, string? _) =>
                Result<CollectionActivityFeedDto>.Success(new(from, to, "basis", "LatestCorrected", DateTime.UtcNow, [corrected], 1, false, 0m, 0m, -600m, 0m, [])));
        _activity.Setup(c => c.ReverseAsync(It.IsAny<HeadReverseCollectionRequest>()))
            .ReturnsAsync(Result<HeadCorrectionResultDto>.Success(new(Guid.NewGuid(), CollectionId, "SRC-2026-000003", 600m, false)));
        page.Find(".txn-row").Click();
        page.WaitForAssertion(() => Assert.Contains("Reverse this collection", page.Find(".txr").TextContent), Timeout);
        page.FindAll(".txr .v3-btn-sm").Single(b => b.TextContent.Contains("Reverse this collection")).Click();
        page.Find(".txn-reason").Input("Wrong month");
        page.Find(".txn-ack input").Change(true);
        page.Find(".txr .v3-btn-primary").Click();

        page.WaitForAssertion(() => Assert.Contains("Reversed", page.Find(".txn-status-line").TextContent), Timeout);
        var drawer = page.Find(".txr").TextContent;
        Assert.DoesNotContain("Eligible", drawer);                                         // the stale pre-reversal message is gone
        Assert.Empty(page.FindAll(".txn-reason")); Assert.Empty(page.FindAll(".txn-ack"));
        Assert.DoesNotContain("Reverse collection", drawer); Assert.DoesNotContain("Reverse this collection", drawer);
        Assert.Contains("Wrong month", drawer);                                            // correction evidence from the reloaded feed
        Assert.Contains("SRC-2026-000003", drawer);                                        // the original SRC is kept
        _activity.Verify(c => c.ReverseAsync(It.IsAny<HeadReverseCollectionRequest>()), Times.Once);
    }

    [Fact]
    public void A_receipt_already_corrected_in_another_tab_opens_as_Reversed_with_no_controls()
    {
        var page = Render(new CollectionReviewDto(CollectionId, "SRC-2026-000003", PhilippineTime.Today, false, true,
            new(false, "AlreadyCorrected", "A correction is already recorded for this collection.")), Canonical());
        page.Find(".txn-row").Click();

        page.WaitForAssertion(() => Assert.Contains("Reversed", page.Find(".txn-status-line").TextContent), Timeout);
        Assert.Empty(page.FindAll(".txn-reason"));
        Assert.DoesNotContain("Reverse this collection", page.Find(".txr").TextContent);
    }

    [Fact]
    public void A_stale_tab_that_tries_anyway_is_told_the_server_refused()
    {
        var page = Render(Review(true), Canonical());
        _activity.Setup(c => c.ReverseAsync(It.IsAny<HeadReverseCollectionRequest>()))
            .ReturnsAsync(Result<HeadCorrectionResultDto>.Failure("AlreadyCorrected", ResultStatus.Conflict));
        page.Find(".txn-row").Click();
        page.WaitForAssertion(() => Assert.Contains("Reverse this collection", page.Find(".txr").TextContent), Timeout);
        page.FindAll(".txr .v3-btn-sm").Single(b => b.TextContent.Contains("Reverse this collection")).Click();
        page.Find(".txn-reason").Input("Late");
        page.Find(".txn-ack input").Change(true);
        page.Find(".txr .v3-btn-primary").Click();

        page.WaitForAssertion(() => Assert.Contains("already recorded", page.Find(".txn-error").TextContent), Timeout);
        Assert.Empty(page.FindAll(".txn-success"));
    }

    [Fact]
    public void Reversal_needs_a_reason_and_confirmation_and_a_lost_reply_retries_with_the_same_operation()
    {
        var sent = new List<HeadReverseCollectionRequest>();
        var page = Render(Review(true), Canonical());
        _activity.Setup(c => c.ReverseAsync(It.IsAny<HeadReverseCollectionRequest>()))
            .Returns((HeadReverseCollectionRequest r) =>
            {
                sent.Add(r);
                return sent.Count == 1 ? throw new HttpRequestException("Synthetic lost reply")
                    : Task.FromResult(Result<HeadCorrectionResultDto>.Success(new(Guid.NewGuid(), CollectionId, "SRC-2026-000003", 600m, true)));
            });
        page.Find(".txn-row").Click();
        page.WaitForAssertion(() => Assert.Contains("Reverse this collection", page.Find(".txr").TextContent), Timeout);
        Assert.Empty(page.FindAll(".txn-reason"));                                         // nothing to fill in until the Head chooses to
        page.FindAll(".txr .v3-btn-sm").Single(b => b.TextContent.Contains("Reverse this collection")).Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".txn-reason")), Timeout);

        Assert.True(page.Find(".txr .v3-btn-primary").HasAttribute("disabled"));     // nothing entered yet
        page.Find(".txn-reason").Input("Wrong month");
        Assert.True(page.Find(".txr .v3-btn-primary").HasAttribute("disabled"));     // reason without confirmation
        page.Find(".txn-ack input").Change(true);
        Assert.False(page.Find(".txr .v3-btn-primary").HasAttribute("disabled"));

        page.Find(".txr .v3-btn-primary").Click();
        page.WaitForAssertion(() => Assert.Contains("reply was lost", page.Find(".txn-error").TextContent), Timeout);
        page.Find(".txr .v3-btn-primary").Click();
        page.WaitForAssertion(() => Assert.Contains("Reversed", page.Find(".txn-status-line").TextContent), Timeout);

        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0], sent[1]);                                                    // same operation, same reason
        Assert.Equal(("Wrong month", CollectionId), (sent[0].Reason, sent[0].CollectionId));
        Assert.NotEqual(Guid.Empty, sent[0].ClientOperationId);
    }
}
