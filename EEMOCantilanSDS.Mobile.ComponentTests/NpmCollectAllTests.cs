using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>NPM Daily Collect All: tick today's stalls, untick exceptions, quote and record with the server; every stall keeps its own SRC.</summary>
public sealed class NpmCollectAllTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private readonly Mock<IMobileApiClient> _api = new();
    private int _recordedCalls;
    private static readonly Guid S1 = Guid.NewGuid(), S2 = Guid.NewGuid(), S3 = Guid.NewGuid(), S4 = Guid.NewGuid();

    private static Result<NpmDailyBatchReadiness> Ready(NpmDailyBatchSource[] sources) => Result<NpmDailyBatchReadiness>.Success(new(Today, sources));

    public NpmCollectAllTests()
    {
        Services.AddSingleton(_api.Object);
        _api.Setup(x => x.GetNpmDailyBatchReadinessAsync()).ReturnsAsync(Ready([
            new(S1, Guid.NewGuid(), "1", "Ana Reyes", 35m, true, null),
            new(S2, Guid.NewGuid(), "2", "Juan Dela Cruz", 35m, true, null),
            new(S3, Guid.NewGuid(), "3", "Maria Santos", 40m, true, null),
            new(S4, Guid.NewGuid(), "4", "Not Today", 35m, false, "AlreadyCollected")]));
    }

    private IRenderedComponent<NpmCollectAll> Open()
    {
        var view = RenderComponent<NpmCollectAll>(p => p.Add(x => x.BusinessDate, Today).Add(x => x.OnRecorded, () => _recordedCalls++));
        view.Find("button.ca-open").Click();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".ca-row")));
        return view;
    }

    [Fact]
    public void ShowsTodaysCollectableStallsAllTicked_WithTheServersChargeAndNeverAHardCodedAmount()
    {
        var view = Open();

        var rows = view.FindAll(".ca-row").Select(r => r.TextContent).ToList();
        Assert.Equal(3, rows.Count);                                                      // the stall the server will not collect is not offered
        Assert.Contains(rows, r => r.Contains("Stall 1") && r.Contains("Ana Reyes") && r.Contains("₱35.00"));
        Assert.Contains(rows, r => r.Contains("Stall 3") && r.Contains("₱40.00"));
        Assert.All(view.FindAll(".ca-row input[type=checkbox]"), c => Assert.True(c.HasAttribute("checked")));
        Assert.Contains("1 not collectible today", view.Markup);
        var summary = view.FindAll(".sheet-meta").Select(m => m.TextContent).ToList();
        Assert.Contains(summary, m => m.Contains("Selected") && m.Contains("3"));
        Assert.Contains(summary, m => m.Contains("Total") && m.Contains("₱110.00"));
        Assert.Contains("Collect 3 stalls", view.Find(".btn-primary").TextContent);
        Assert.NotNull(view.Find(".ca-list"));                                             // bounded, scrolling list
    }

    [Fact]
    public void UntickingAStallLeavesItOutOfTheBatch_WithoutMarkingAnyoneAbsent()
    {
        var view = Open();

        view.FindAll(".ca-row input[type=checkbox]")[1].Change(false);

        Assert.Contains("Collect 2 stalls", view.Find(".btn-primary").TextContent);
        Assert.Contains("₱75.00", view.FindAll(".sheet-meta").Last().TextContent);
        Assert.DoesNotContain("absent", view.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectQuotesOnlyTheTickedStalls_RecordsWithTheFingerprint_AndListsEverySrcAfterTheTotal()
    {
        NpmDailyBatchIntent? quoted = null;
        _api.Setup(x => x.QuoteNpmDailyBatchAsync(It.IsAny<NpmDailyBatchIntent>())).ReturnsAsync((NpmDailyBatchIntent i) =>
        {
            quoted = i;
            var sources = i.Items.Select(item => new NpmDailyBatchSource(item.StallId, Guid.NewGuid(), item.StallId == S1 ? "1" : "3", item.StallId == S1 ? "Ana Reyes" : "Maria Santos", item.StallId == S1 ? 35m : 40m, true, null)).ToArray();
            return Result<NpmDailyBatchQuote>.Success(new(i, sources, sources.Sum(x => x.EffectiveCharge), "fingerprint", []));
        });
        RecordNpmDailyBatchRequest? recorded = null;
        _api.Setup(x => x.RecordNpmDailyBatchAsync(It.IsAny<RecordNpmDailyBatchRequest>())).ReturnsAsync((RecordNpmDailyBatchRequest r) =>
        {
            recorded = r;
            return Result<CollectionSessionResult>.Success(new(r.Intent.ClientCollectionSessionId, CollectionSessionStatus.Recorded, null, 75m,
                r.Intent.Items.Select((x, n) => new CollectionSessionCollection(Guid.NewGuid(), $"SRC-2026-0001{n}0", RevenueInstrumentType.OfficialReceipt,
                    x.StallId == S1 ? 35m : 40m, [x.ClientItemId], "Posted")).ToArray(), []));
        });
        var view = Open();
        view.FindAll(".ca-row input[type=checkbox]")[1].Change(false);          // Stall 2 is the exception

        view.Find(".btn-primary").Click();

        view.WaitForAssertion(() =>
        {
            Assert.NotNull(quoted); Assert.NotNull(recorded);
            Assert.Equal(new[] { S1, S3 }, quoted!.Items.Select(x => x.StallId).ToArray());       // only the ticked stalls
            Assert.Equal("fingerprint", recorded!.QuoteFingerprint);
            Assert.True(quoted.Items.Select(x => x.ClientItemId).Distinct().Count() == 2);
            Assert.Contains("₱75.00", view.Find(".done-total").TextContent);                       // the total first
            var results = view.FindAll(".ca-result").Select(r => r.TextContent).ToList();
            Assert.Contains(results, r => r.Contains("Stall 1") && r.Contains("Ana Reyes") && r.Contains("SRC-2026-000100"));
            Assert.Contains(results, r => r.Contains("Stall 3") && r.Contains("SRC-2026-000110"));
            Assert.DoesNotContain("Juan Dela Cruz", view.Find(".ca-list").TextContent);              // the unticked stall received nothing
        });
        Assert.Equal(0, _recordedCalls);                                                           // the round behind is not reloaded while the SRC list is on screen
        view.Find(".btn-cancel").Click();
        Assert.Equal(1, _recordedCalls);                                                           // ...only when the collector is done
    }

    [Fact]
    public void WhenTheQuoteCannotBeReached_TheSelectionIsKept_AndNothingIsRecorded()
    {
        _api.Setup(x => x.QuoteNpmDailyBatchAsync(It.IsAny<NpmDailyBatchIntent>())).ReturnsAsync(Result<NpmDailyBatchQuote>.Failure("offline"));
        var view = Open();
        view.FindAll(".ca-row input[type=checkbox]")[0].Change(false);

        view.Find(".btn-primary").Click();

        view.WaitForAssertion(() => Assert.Contains("Your selection is kept", view.Find(".form-error").TextContent));
        _api.Verify(x => x.RecordNpmDailyBatchAsync(It.IsAny<RecordNpmDailyBatchRequest>()), Times.Never);
        Assert.Contains("Collect 2 stalls", view.Find(".btn-primary").TextContent);
    }

    [Fact]
    public void AChangedServerTotalIsShownFirst_AndNeedsASecondTapBeforeAnythingIsRecorded()
    {
        _api.Setup(x => x.QuoteNpmDailyBatchAsync(It.IsAny<NpmDailyBatchIntent>())).ReturnsAsync((NpmDailyBatchIntent i) =>
            Result<NpmDailyBatchQuote>.Success(new(i, [], 150m, "fp", [])));
        // The quote answers with items so it can be recorded; its total (150) differs from the 110 on screen.
        _api.Setup(x => x.QuoteNpmDailyBatchAsync(It.IsAny<NpmDailyBatchIntent>())).ReturnsAsync((NpmDailyBatchIntent i) =>
            Result<NpmDailyBatchQuote>.Success(new(i, [new(S1, Guid.NewGuid(), "1", "Ana Reyes", 150m, true, null)], 150m, "fp", [])));
        var view = Open();

        view.Find(".btn-primary").Click();

        view.WaitForAssertion(() => Assert.Contains("₱150.00", view.FindAll(".sheet-meta").Last().TextContent));
        _api.Verify(x => x.RecordNpmDailyBatchAsync(It.IsAny<RecordNpmDailyBatchRequest>()), Times.Never);
    }

    [Fact]
    public void TheActionIsSolidAndCountsWhatTheServerSaysCanStillBeCollected_EvenWhenOnlyOneRemains()
    {
        _api.Setup(x => x.GetNpmDailyBatchReadinessAsync()).ReturnsAsync(Ready([
            new(S1, Guid.NewGuid(), "1", "Ana Reyes", 35m, false, "AlreadyCollected"),
            new(S2, Guid.NewGuid(), "2", "Juan Dela Cruz", 35m, true, null)]));
        var view = RenderComponent<NpmCollectAll>(p => p.Add(x => x.BusinessDate, Today));

        view.WaitForAssertion(() => Assert.Equal("Collect All · 1 stall", view.Find("button.ca-open").TextContent.Trim()));
        Assert.False(view.Find("button.ca-open").HasAttribute("disabled"));
        view.Find("button.ca-open").Click();
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".ca-row")));                       // the collected stall is not offered again
        Assert.Contains("Collect 1 stall", view.Find(".btn-primary").TextContent);
    }

    [Fact]
    public void WhenNothingCanBeCollectedTheActionIsPlainlyDisabled()
    {
        _api.Setup(x => x.GetNpmDailyBatchReadinessAsync()).ReturnsAsync(Ready([
            new(S1, Guid.NewGuid(), "1", "Ana Reyes", 35m, false, "AlreadyCollected")]));
        var view = RenderComponent<NpmCollectAll>(p => p.Add(x => x.BusinessDate, Today));

        view.WaitForAssertion(() => Assert.True(view.Find("button.ca-open").HasAttribute("disabled")));
        Assert.Contains("Nothing left to collect today", view.Find("button.ca-open").TextContent);
    }
}
