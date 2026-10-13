using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>
/// The Collector's position reads as three cash figures and, apart from them, the paper forms on hand. The figures are the server's, the
/// repeated explanatory paragraphs are gone (the meaning is a tooltip), and holding unused tickets is never shown as money or as a blocker.
/// </summary>
public sealed class CollectorPositionPanelTests : TestContext
{
    private readonly Mock<IMobileApiClient> _api = new();
    private static readonly DateOnly From = new(2026, 10, 1), To = new(2026, 10, 31);

    public CollectorPositionPanelTests()
    {
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("tenant:collector");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
    }

    private static CollectorPositionDto Position(decimal collected, decimal remitted, params FormAccountabilityDto[] forms) =>
        new(Guid.NewGuid(), "Pat", collected, remitted, collected - remitted, 0, 0m, forms);

    private IRenderedComponent<CollectorPositionPanel> Open(CollectorPositionDto position)
    {
        _api.Setup(x => x.GetMyPositionAsync(From, To)).ReturnsAsync(Result<CollectorPositionDto>.Success(position));
        var view = RenderComponent<CollectorPositionPanel>(p => p.Add(x => x.From, From).Add(x => x.To, To));
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".cpp-fig")));
        return view;
    }

    [Fact]
    public void CollectedRemittedAndUnremittedAreThreeFiguresWithoutExplanatoryParagraphs()
    {
        var view = Open(Position(137m, 0m));

        var figures = view.FindAll(".cpp-fig").Select(f => (f.QuerySelector("dt")!.TextContent, f.QuerySelector("dd")!.TextContent)).ToArray();
        Assert.Equal([("Collected", "₱137.00"), ("Remitted", "₱0.00"), ("Unremitted", "₱137.00")], figures);
        Assert.Empty(view.FindAll("p").Where(p => p.TextContent.Contains("Remittance is recorded by the office")));
        Assert.Contains("not yet turned over", view.Find(".cpp-fig.strong").GetAttribute("title"));
    }

    [Fact]
    public void UnusedFormsAreAPaperCountApartFromTheCash_AndNeverAMoneyFigureOrABlocker()
    {
        var view = Open(Position(100m, 100m, new FormAccountabilityDto(RevenueInstrumentType.CashTicket, 50, 10, 0, 0, 0, 40)));

        Assert.Equal("₱0.00", view.FindAll(".cpp-fig").Single(f => f.QuerySelector("dt")!.TextContent == "Unremitted").QuerySelector("dd")!.TextContent);   // cash is settled
        var forms = view.Find(".cpp-rows").TextContent;
        Assert.Contains("Cash Tickets", forms); Assert.Contains("40 on hand", forms); Assert.Contains("Used 10", forms);
        Assert.DoesNotContain("₱40", forms);
        Assert.DoesNotContain("never pesos", view.Markup);
    }

    [Fact]
    public void AFailedLoadIsStatedWithARetry_NeverZeroFigures()
    {
        _api.Setup(x => x.GetMyPositionAsync(From, To)).ReturnsAsync(Result<CollectorPositionDto>.Failure("offline"));
        var view = RenderComponent<CollectorPositionPanel>(p => p.Add(x => x.From, From).Add(x => x.To, To));
        view.WaitForAssertion(() => Assert.Contains("Could not load your position", view.Find("[role=alert]").TextContent));
        Assert.Empty(view.FindAll(".cpp-fig"));
        Assert.Equal("Try again", view.Find(".cpp-retry").TextContent.Trim());
    }
}
