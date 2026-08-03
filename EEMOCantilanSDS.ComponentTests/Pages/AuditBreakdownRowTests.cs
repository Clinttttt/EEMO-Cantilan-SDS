using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AuditBreakdownRow = EEMOCantilanSDS.Client.Components.Pages.Shared.AuditBreakdownRow;

/// <summary>
/// bUnit render tests for the shared Audit Breakdown row used by the fixed-rent facility reports
/// (TCC / NCC / BBQ / ICE and LGU-defined facilities). The office reconciles these three cards by hand,
/// so the test pins what must stay true of the markup: billed = collected + open, the bars never exceed
/// the track, the compliance percentages total 100, and the two headline figures repeat the same money.
/// </summary>
public class AuditBreakdownRowTests : TestContext
{
    private IRenderedComponent<AuditBreakdownRow> Render(
        decimal billed, decimal collected, decimal outstanding,
        int paid, int partial, int unpaid, int rate, string? period = "May 2025")
    {
        // The global _Imports.razor injects these into every component; stub them so the row resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<AuditBreakdownRow>(p => p
            .Add(c => c.Billed, billed)
            .Add(c => c.Collected, collected)
            .Add(c => c.Outstanding, outstanding)
            .Add(c => c.PaidCount, paid)
            .Add(c => c.PartialCount, partial)
            .Add(c => c.UnpaidCount, unpaid)
            .Add(c => c.CollectionRate, rate)
            .Add(c => c.PeriodLabel, period));
    }

    [Fact]
    public void Reconciles_BilledCollectedAndOpen_WithMatchingPercentages()
    {
        var cut = Render(billed: 27_001m, collected: 0m, outstanding: 27_001m,
                         paid: 0, partial: 0, unpaid: 11, rate: 0);

        Assert.Contains("₱27,001", cut.Markup);
        Assert.Contains("₱0", cut.Markup);
        // Billed is the whole of the period, open is all of it, collected none of it.
        Assert.Equal("width:100%", cut.Find(".fm-billed").GetAttribute("style"));
        Assert.Equal("width:0%", cut.Find(".fm-collected").GetAttribute("style"));
        Assert.Equal("width:100%", cut.Find(".fm-open").GetAttribute("style"));
        // The headline figures state the outstanding money and the rate, and nothing else: the rate is
        // assessed against rent due while the money above includes utilities, so printing "₱X of ₱Y" beside
        // the rate read as one reconciliation when the two do not share a base.
        Assert.DoesNotContain("of ₱27,001 collected", cut.Markup);
        Assert.Contains("Total Outstanding", cut.Markup);
        Assert.Contains("Collection Rate", cut.Markup);
    }

    [Fact]
    public void PartialCollection_ShowsProportionalBars_NeverOverfilled()
    {
        var cut = Render(billed: 10_000m, collected: 7_500m, outstanding: 2_500m,
                         paid: 6, partial: 2, unpaid: 2, rate: 75);

        Assert.Equal("width:75%", cut.Find(".fm-collected").GetAttribute("style"));
        Assert.Equal("width:25%", cut.Find(".fm-open").GetAttribute("style"));

        var pcts = cut.FindAll(".cmp-pct").Select(e => int.Parse(e.TextContent.Trim().TrimEnd('%'))).ToList();
        Assert.Equal(new[] { 60, 20, 20 }, pcts);
        Assert.Equal(100, pcts.Sum());
        Assert.Contains("Total payors: 10", cut.Markup);
    }

    [Fact]
    public void NothingBilled_ReportsZero_NotAFullBar()
    {
        var cut = Render(billed: 0m, collected: 0m, outstanding: 0m,
                         paid: 0, partial: 0, unpaid: 0, rate: 0);

        // The earlier markup hard-coded the billed row at 100%, which read as a full month billed
        // on a facility with nothing to bill.
        Assert.Equal("width:0%", cut.Find(".fm-billed").GetAttribute("style"));
        Assert.Contains("No payors assessed for this period", cut.Markup);
    }

    [Fact]
    public void OverCollection_IsCappedAtTheTrack()
    {
        // Over-collection is revenue, never a negative balance — the bar stops at the track edge.
        var cut = Render(billed: 1_000m, collected: 1_400m, outstanding: 0m,
                         paid: 1, partial: 0, unpaid: 0, rate: 100);

        Assert.Equal("width:100%", cut.Find(".fm-collected").GetAttribute("style"));
    }

    [Fact]
    public void CountedSubject_IsCallerNamed_ForFacilitiesThatDoNotCollectFromPayors()
    {
        var cut = RenderWithSubject("Trip OR Compliance", "Operators");

        Assert.Contains("Trip OR Compliance", cut.Markup);
        Assert.Contains("Operators by compliance status", cut.Markup);
        Assert.Contains("Total operators: 3", cut.Markup);
    }

    private IRenderedComponent<AuditBreakdownRow> RenderWithSubject(string title, string plural)
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<AuditBreakdownRow>(p => p
            .Add(c => c.Billed, 900m)
            .Add(c => c.Collected, 900m)
            .Add(c => c.Outstanding, 0m)
            .Add(c => c.PaidCount, 3)
            .Add(c => c.PartialCount, 0)
            .Add(c => c.UnpaidCount, 0)
            .Add(c => c.CollectionRate, 100)
            .Add(c => c.ComplianceTitle, title)
            .Add(c => c.EntityPlural, plural));
    }
}
