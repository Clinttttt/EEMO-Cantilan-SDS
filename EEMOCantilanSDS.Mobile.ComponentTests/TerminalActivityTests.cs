using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Mobile.Components.Shared;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>Today's Terminal collections: the server's canonical activity, only the facts that exist, and never an edit or delete of a posted Collection.</summary>
public sealed class TerminalActivityTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private readonly Mock<IMobileApiClient> _api = new();

    private static SourceNativeActivityDto Row(string src, TerminalSection section, decimal amount, int? tickets = null, string? vehicle = null, string state = "Posted", decimal? net = null) =>
        new(Guid.NewGuid(), src, Today, new DateTime(2026, 10, 7, 2, 42, 0, DateTimeKind.Utc), CollectorOperationCodes.Terminal, section, null, null,
            Guid.NewGuid(), "Cora Collector", amount, net ?? amount, state, tickets, vehicle is null ? null : Guid.NewGuid(), null, vehicle is null ? null : 20m, null, vehicle);

    private IRenderedComponent<TerminalActivity> Open(params SourceNativeActivityDto[] rows)
    {
        _api.Setup(x => x.GetOfficeActivityAsync(Today, Today, CollectorOperationCodes.Terminal)).ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success(rows));
        Services.AddSingleton(_api.Object);
        var view = RenderComponent<TerminalActivity>(p => p.Add(x => x.BusinessDate, Today));
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".ta-note[role=status]")));
        return view;
    }

    [Fact]
    public void ListsTodaysTerminalCollections_WithOnlyTheFactsThatExist()
    {
        var view = Open(Row("SRC-2026-000021", TerminalSection.PullPulVansCargoVans, 5800m, 24, "Jeepney"),
                        Row("SRC-2026-000022", TerminalSection.Tricycad, 3200m));

        var rows = view.FindAll(".ta-row").Select(r => r.TextContent).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Contains("PULL PUL VANS, CARGO VANS") && r.Contains("₱5,800.00") && r.Contains("Jeepney") && r.Contains("24 CT") && r.Contains("SRC-2026-000021"));
        var aggregate = rows.Single(r => r.Contains("TRICYCAD"));
        Assert.Contains("Section total", aggregate);
        Assert.DoesNotContain("CT", aggregate.Replace("SRC", ""));                                   // no ticket count was recorded, so none is shown
        Assert.DoesNotContain("Jeepney", aggregate);                                                // and no vehicle type is invented
    }

    [Fact]
    public void TappingARow_ShowsItsDetail_AndOffersNoEditOrDeleteOfAPostedCollection()
    {
        var view = Open(Row("SRC-2026-000021", TerminalSection.PullPulVansCargoVans, 5800m, 24, "Jeepney"));

        view.Find(".ta-row").Click();

        var sheet = view.Find("[role=dialog]").TextContent;
        foreach (var fact in new[] { "PULL PUL VANS, CARGO VANS", "Jeepney", "₱5,800.00", "CT · Cash Ticket", "24", "Cora Collector", "SRC-2026-000021", "Correct collection" })
            Assert.Contains(fact, sheet);
        var buttons = view.FindAll("[role=dialog] button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["Close"], buttons);                                                            // nothing here can delete or rewrite the posted Collection
        Assert.Contains("ask the office", sheet);
    }

    [Fact]
    public void ACorrectedCollection_ShowsTheNetAmountAndSaysTheOfficeCorrectedIt()
    {
        var view = Open(Row("SRC-2026-000023", TerminalSection.ComfortRoom, 400m, state: "Reversed", net: 0m));

        Assert.Contains("Corrected", view.Find(".ta-row").TextContent);
        view.Find(".ta-row").Click();
        Assert.Contains("The office has corrected this collection", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void WithoutAnyCollectionToday_SaysSo()
    {
        var view = RenderWithNone();
        Assert.Contains("No Terminal collection recorded today", view.Markup);
    }

    private IRenderedComponent<TerminalActivity> RenderWithNone()
    {
        _api.Setup(x => x.GetOfficeActivityAsync(Today, Today, CollectorOperationCodes.Terminal)).ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]));
        Services.AddSingleton(_api.Object);
        var view = RenderComponent<TerminalActivity>(p => p.Add(x => x.BusinessDate, Today));
        view.WaitForAssertion(() => Assert.Contains("No Terminal collection", view.Markup));
        return view;
    }
}
