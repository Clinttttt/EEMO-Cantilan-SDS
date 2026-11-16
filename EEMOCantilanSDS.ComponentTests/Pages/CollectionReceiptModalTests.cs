using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Pages.Shared.Actions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>A facility row's View / Record Payment on a month with nothing paid used to open a receipt for ₱0.</summary>
public sealed class CollectionReceiptModalTests : TestContext
{
    public CollectionReceiptModalTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    private IRenderedComponent<CollectionReceiptModal> Render(bool nothingPaid) => RenderComponent<CollectionReceiptModal>(p => p
        .Add(x => x.Show, true)
        .Add(x => x.PeriodLabel, "November 2026")
        .Add(x => x.TotalLabel, "Total Paid")
        .Add(x => x.TotalValue, "₱0.00")
        .Add(x => x.NoteText, "This acknowledges receipt of the monthly rental payment stated above.")
        .Add(x => x.NothingPaid, nothingPaid)
        .Add(x => x.RecordHref, "/reports/collections?facility=TCC&stall=1"));

    [Fact]
    public void A_month_with_nothing_paid_says_so_and_acknowledges_no_receipt()
    {
        var cut = Render(nothingPaid: true);

        Assert.Contains("No payment is recorded for November 2026", cut.Find(".receipt-empty").TextContent);
        Assert.Equal("/reports/collections?facility=TCC&stall=1", cut.Find(".receipt-empty a").GetAttribute("href"));
        Assert.DoesNotContain("acknowledges receipt", cut.Markup);
        Assert.Empty(cut.FindAll(".receipt-grand"));
    }

    [Fact]
    public void A_paid_month_still_shows_its_receipt()
    {
        var cut = Render(nothingPaid: false);

        Assert.Empty(cut.FindAll(".receipt-empty"));
        Assert.Contains("acknowledges receipt", cut.Find(".receipt-note").TextContent);
    }
}
