using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// "Collected today" on the Financial Summary: the office's money for today's Philippine date, read from the Collection Activity reader (each legacy and
/// canonical collection once, corrections included). It is dated, it does not move with the period the summary is filtered to, and it shows a dash —
/// never ₱0.00 — when it could not be read.
/// </summary>
public sealed class SummaryCollectedTodayTests : TestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly Mock<ICollectionActivityApiClient> _activity = new();
    private readonly Mock<IRemittancesApiClient> _remittances = new();

    public SummaryCollectedTodayTests()
    {
        Services.AddSingleton(_activity.Object);
        Services.AddSingleton(_remittances.Object);
        Services.AddSingleton(Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        _remittances.Setup(r => r.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Failure("not needed here", 500));
    }

    private static CollectionActivityFeedDto Feed(decimal legacy, decimal canonical, decimal correction) =>
        new(PhilippineTime.Today, PhilippineTime.Today, "Business date", "Latest corrected", DateTime.UtcNow, [], 0, false,
            legacy, canonical, correction, legacy + canonical + correction, []);

    private IRenderedComponent<SummaryCashPosition> Render(DateOnly? from = null, DateOnly? to = null) => RenderComponent<SummaryCashPosition>(p => p
        .Add(x => x.Collected, 9_999m).Add(x => x.YtdCollected, 50_000m)
        .Add(x => x.From, from ?? new DateOnly(2026, 8, 1)).Add(x => x.To, to ?? new DateOnly(2026, 8, 31)));

    private static string Figure(IRenderedComponent<SummaryCashPosition> cut, string label, bool exactOnly = false) =>
        cut.FindAll(".ss-item").Single(i => i.QuerySelector("dt")!.TextContent.Trim() == label || (exactOnly is false && i.QuerySelector("dt")!.TextContent.StartsWith(label))).QuerySelector("dd")!.TextContent.Trim();

    [Fact]
    public void ShowsTodaysNetCollections_WithTheExactDate_AsTheLastFigureAfterAccountabilityExceptions()
    {
        // ₱1,000 legacy + ₱500 canonical − ₱100 reversed: the net, not the gross.
        _activity.Setup(a => a.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(Feed(1_000m, 500m, -100m)));

        var cut = Render();

        cut.WaitForAssertion(() => Assert.Equal("₱1,400.00", Figure(cut, "Collected today")), Wait);
        var labels = cut.FindAll(".ss-item dt").Select(d => d.TextContent.Trim()).ToList();
        Assert.Equal("Accountability exceptions", labels[^2]);
        Assert.Equal($"Collected today · {PhilippineTime.Today:MMM d, yyyy}", labels[^1]);        // the exact date: it can never be read as the period's figure
    }

    [Fact]
    public void AsksForExactlyTodaysPhilippineDate_WhateverPeriodTheSummaryIsFilteredTo_AndNeverMovesWithIt()
    {
        _activity.Setup(a => a.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(Feed(0m, 250m, 0m)));

        var cut = Render(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));                  // a previous year is selected

        cut.WaitForAssertion(() => Assert.Equal("₱250.00", Figure(cut, "Collected today")), Wait);
        var today = PhilippineTime.Today;
        _activity.Verify(a => a.GetAsync(today, today, null, null, null, It.IsAny<int>(), null), Times.Once);
        _activity.Verify(a => a.GetAsync(It.Is<DateOnly>(d => d != today), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null), Times.Never);
        Assert.Equal("₱9,999.00", Figure(cut, "Collected", exactOnly: true));                                     // the period's own figure is unrelated and unchanged
    }

    [Fact]
    public void AZeroDayReadsAsAZero_ButAFailedReadReadsAsADash_NeverAMisleadingZero()
    {
        _activity.Setup(a => a.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(Feed(0m, 0m, 0m)));
        var zero = Render();
        zero.WaitForAssertion(() => Assert.Equal("₱0.00", Figure(zero, "Collected today")), Wait);
        Assert.DoesNotContain("Today's collections could not be loaded", zero.Markup);

        var failing = new Mock<ICollectionActivityApiClient>();
        failing.Setup(a => a.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Failure("Unavailable", 503));
        using var other = new TestContext();
        other.Services.AddSingleton(failing.Object);
        other.Services.AddSingleton(_remittances.Object);
        other.Services.AddSingleton(Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IMunicipalitiesApiClient>());
        other.Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        other.Services.AddSingleton(Mock.Of<ISetupApiClient>());
        other.Services.AddSingleton(Mock.Of<IStallsApiClient>());
        other.Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        var down = other.RenderComponent<SummaryCashPosition>(p => p.Add(x => x.Collected, 1m)
            .Add(x => x.From, new DateOnly(2026, 8, 1)).Add(x => x.To, new DateOnly(2026, 8, 31)));

        down.WaitForAssertion(() => Assert.Contains("Today's collections could not be loaded", down.Markup), Wait);
        Assert.Equal("—", Figure(down, "Collected today"));
    }

    [Fact]
    public void ARepeatedRenderWithinTheRefreshWindowDoesNotReadTheFeedAgain()
    {
        _activity.Setup(a => a.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(Feed(0m, 100m, 0m)));
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Equal("₱100.00", Figure(cut, "Collected today")), Wait);

        cut.SetParametersAndRender(p => p.Add(x => x.Collected, 1m).Add(x => x.YtdCollected, 2m));
        cut.SetParametersAndRender(p => p.Add(x => x.Collected, 3m).Add(x => x.YtdCollected, 4m));

        _activity.Verify(a => a.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null), Times.Once);
    }
}
