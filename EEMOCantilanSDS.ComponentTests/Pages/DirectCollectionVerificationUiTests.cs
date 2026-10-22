using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Verification on the direct-collection review: three separate cash figures, Head-only verification, one rule set for what can be confirmed.</summary>
public sealed class DirectCollectionVerificationUiTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IDirectCollectionReviewApiClient> _api = new();

    public DirectCollectionVerificationUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IOfficeSourcesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    private static DirectCollectionReviewRowDto Row(string src, string state = "Awaiting review", string disposition = "Posted", string? block = null,
        bool duplicate = false, string verification = "Awaiting verification", decimal net = 656m) =>
        new(Guid.NewGuid(), src, new DateOnly(2026, 10, 12), new DateTime(2026, 10, 12, 10, 14, 0, DateTimeKind.Utc), "Electricity Consumption Fees", "ECF",
            "Juan Dela Cruz", "Public Market · Stall V-01", 656m, "Pat Collector", "Direct", "Source verified", disposition, state, null, null, null,
            duplicate, block is null, null, block, verification, net);

    private void Serve(bool canVerify, params DirectCollectionReviewRowDto[] rows)
    {
        _api.Setup(x => x.GetAsync(It.IsAny<DirectCollectionReviewRequest>())).ReturnsAsync(Result<DirectCollectionReviewPageDto>.Success(
            new(rows, rows.Length, rows.Count(r => r.ReviewState == "Awaiting review"), 0, canVerify)));
        _api.Setup(x => x.PositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<bool>())).ReturnsAsync(Result<DirectCollectionVerificationPositionDto>.Success(
            new(new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12), 1000m, 656m, 344m, 200m, 100m, 44m, 3, 1, 2)));
    }

    private IRenderedComponent<DirectCollectionReview> Open()
    {
        var cut = RenderComponent<DirectCollectionReview>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".dcr-trigger")), Timeout);
        cut.Find(".dcr-trigger").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        return cut;
    }

    [Fact]
    public void Recorded_verified_and_pending_cash_are_three_separate_figures_that_add_up_and_say_cash_is_counted_regardless()
    {
        Serve(true, Row("SRC-2026-000016"));
        var cut = Open();

        var figures = cut.Find("dl[aria-label='Direct collection cash']").TextContent;
        Assert.Contains("Recorded", figures); Assert.Contains("1,000.00", figures);
        Assert.Contains("Verified", figures); Assert.Contains("656.00", figures);
        Assert.Contains("Pending verification", figures); Assert.Contains("344.00", figures);
    }

    [Fact]
    public void An_Admin_reads_and_can_flag_but_sees_no_confirm_resolve_or_bulk_control()
    {
        Serve(false, Row("SRC-2026-000016"), Row("SRC-2026-000017", state: "Needs correction", block: "CorrectionUnresolved"));
        var cut = Open();

        Assert.Empty(cut.FindAll("tbody input[type=checkbox]"));
        Assert.DoesNotContain(cut.FindAll("tbody button"), b => b.TextContent.Trim() is "Confirm" or "Resolve");
        Assert.Contains(cut.FindAll("tbody button"), b => b.TextContent.Contains("Needs correction"));     // flagging is the one non-financial action left
    }

    [Fact]
    public void The_Head_sees_why_a_collection_cannot_be_confirmed_and_is_not_offered_the_button()
    {
        Serve(true, Row("SRC-2026-000016"), Row("SRC-2026-000017", state: "Needs correction", block: "CorrectionUnresolved"),
            Row("SRC-2026-000018", disposition: "Reversed", block: "Reversed", verification: "Reversed", net: 0m),
            Row("SRC-2026-000019", block: "IdentityUnresolved"));
        var cut = Open();

        var rows = cut.FindAll("tbody tr");
        Assert.Contains("Confirm", rows[0].TextContent);
        foreach (var blocked in rows.Skip(1)) Assert.DoesNotContain(blocked.QuerySelectorAll("button").Select(b => b.TextContent.Trim()), t => t == "Confirm");
        Assert.Contains("Resolve it first", rows[1].TextContent);
        Assert.Contains("Resolve", rows[1].QuerySelectorAll("button").Select(b => b.TextContent.Trim()));
        Assert.Contains("nothing to confirm", rows[2].TextContent);
        Assert.Contains("Register or link the payor first", rows[3].TextContent);
    }

    [Fact]
    public void A_possible_duplicate_is_confirmed_on_its_own_and_only_with_a_written_reason()
    {
        var row = Row("SRC-2026-000016", block: "PossibleDuplicate", duplicate: true);
        Serve(true, row);
        DirectCollectionReviewActionRequest? sent = null;
        _api.Setup(x => x.ReviewAsync(It.IsAny<DirectCollectionReviewActionRequest>())).Callback<DirectCollectionReviewActionRequest>(r => sent = r)
            .ReturnsAsync(Result<DirectCollectionReviewRowDto>.Success(Row("SRC-2026-000016", state: "Confirmed")));
        var cut = Open();

        Assert.Empty(cut.FindAll("tbody input[type=checkbox]"));                                         // never part of a bulk selection
        cut.FindAll("tbody button").Single(b => b.TextContent.Trim() == "Confirm").Click();
        Assert.Null(sent);                                                                               // nothing is sent until a reason is written
        var confirm = () => cut.FindAll("tbody button.v3-btn-primary").Single(b => b.TextContent.Trim() == "Confirm");
        Assert.True(confirm().HasAttribute("disabled"));
        cut.Find("tbody textarea").Input("too short");
        Assert.True(confirm().HasAttribute("disabled"));
        cut.Find("tbody textarea").Input("Second payment received at the counter, separate slip");
        Assert.False(confirm().HasAttribute("disabled"));
        confirm().Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal(("Confirmed", "Second payment received at the counter, separate slip", row.CollectionId), (sent!.Outcome, sent.Note, sent.CollectionId));
    }
}
