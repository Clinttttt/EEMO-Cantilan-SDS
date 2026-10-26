using Bunit;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>A renewed space reviewed term by term, and a never-renewed expired term stated as such — the lifetime balance always reconciled.</summary>
public sealed partial class FollowUpAccountReviewTests
{
    [Fact]
    public void ARenewedSpaceStatesTheCurrentTermAndEarlierTermsApart_AndKeepsTheReconciledLifetimeBalance()
    {
        var account = Account() with
        {
            ActiveTo = null, ContractEffectiveDate = new(2025, 10, 25), ContractYears = 3, AssessedToDate = 4000m, CollectedToDate = 500m, OutstandingToDate = 3500m,
            Terms =
            [
                new(1, new(2022, 10, 25), 1, new(2023, 10, 24), new(2025, 10, 24), "Maria Contract", "KM-1", 2500m, 500m, 2000m, false),
                new(2, new(2025, 10, 25), 3, new(2028, 10, 24), null, "Maria Contract", "KM-2", 1500m, 0m, 1500m, true)
            ]
        };
        Serve(account, Quote(account, new(2025, 9, 1), 1000, 0), Quote(account, new(2025, 10, 1), 1500, 500), Quote(account, new(2026, 3, 1), 1500, 0));

        var cut = RenderComponent<FollowUpAccountReview>(p => p.Add(x => x.Context, ContextFor(account)));

        var summary = cut.Find(".review-summary").TextContent;
        Assert.Contains("Current term balance", summary);
        Assert.Contains("₱1,500.00", summary);                                                       // current term
        Assert.Contains("Earlier terms balance", summary);
        Assert.Contains("₱2,000.00", summary);                                                       // earlier term
        Assert.Contains("₱3,500.00", summary);                                                       // lifetime, reconciled
        var groups = cut.FindAll("section[aria-label='Outstanding billing periods'] > details").Where(d => d.TextContent.Contains("with balance")).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Contains("Current term", groups[0].TextContent);
        Assert.Contains("March 2026", groups[0].TextContent);
        Assert.DoesNotContain("October 2025", groups[0].TextContent);
        Assert.Contains("Earlier term", groups[1].TextContent);
        Assert.Contains("October 2025", groups[1].TextContent);                                     // the month the earlier term ended in stays with it
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public void ANeverRenewedExpiredTermIsStated_WithTheMonthsBilledAfterIt_AndNothingRemoved()
    {
        // Signed Oct 25, 2022 for three years (ended Oct 24, 2025), never renewed and still occupied.
        var account = Account(ObligationKind.VegetableFruitSpaceRental) with
        {
            ActiveTo = null, ContractEffectiveDate = new(2022, 10, 25), ContractYears = 3, AssessedToDate = 3000m, CollectedToDate = 0m, OutstandingToDate = 3000m
        };
        Serve(account, Quote(account, new(2025, 9, 1), 1000, 0), Quote(account, new(2025, 11, 1), 1000, 0), Quote(account, new(2025, 12, 1), 1000, 0));

        var cut = RenderComponent<FollowUpAccountReview>(p => p.Add(x => x.Context, ContextFor(account) with
        {
            ObligationItem = ContextFor(account).ObligationItem! with { Scope = new(EEMOCantilanSDS.Application.Dtos.Reports.FollowUpScopeKind.MonthlySpace, "VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable / Fruits") }
        }));

        var note = cut.Find(".review-term-note").TextContent;
        Assert.Contains("ended on Oct 24, 2025 and has not been renewed", note);
        Assert.Contains("2 months with a balance fall after it ended", note);
        Assert.Contains("₱3,000.00", cut.Find(".review-summary").TextContent);
        Assert.Contains("November 2025", cut.Markup);
    }
}
