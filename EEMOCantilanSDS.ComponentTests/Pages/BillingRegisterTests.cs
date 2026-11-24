using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using BillingRegisterPage = EEMOCantilanSDS.Client.Components.Pages.Reports.BillingRegister;

/// <summary>The Billing Register states each account's Statement of Account figures as the server gives them, and adds up only the totals row.</summary>
public class BillingRegisterTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateOnly AsOf = new(2026, 11, 23);
    private static readonly Guid Rosa = Guid.NewGuid();

    private static BillingRegisterDto Register() => new(FacilityCode.TCC, AsOf, new DateOnly(2026, 11, 20),
    [
        new(Rosa, "D-08", "Rosa Aquino", "", 3, 10_800m, 3_600m, 0m, null, 3_600m, 0m, 792m, 720m, 72m),
        new(Guid.NewGuid(), "D-12", "Nora Santos", "", 0, 0m, 2_400m, 2_400m, "0012345", 0m, 0m, 0m, PaidAheadMonths: 2, PaidAhead: 4_800m, PaidAheadThrough: new DateOnly(2027, 1, 1)),
    ], SurchargePercent: 25m, InterestPercent: 2m, InterestMaxMonths: 36, Authority: "Ordinance No. 7");

    private Mock<IReportsApiClient> Setup(bool catalogRead = true)
    {
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(catalogRead
                ? Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([new(FacilityCode.TCC, "Demo Trade Center", "DTC", 0, null, null, null)])
                : Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("down"));

        var reports = new Mock<IReportsApiClient>();
        reports.Setup(r => r.GetBillingRegisterAsync(FacilityCode.TCC, It.IsAny<int?>(), It.IsAny<int?>())).ReturnsAsync(Result<BillingRegisterDto>.Success(Register()));

        Services.AddSingleton(reports.Object);
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton<BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Head");
        return reports;
    }

    [Fact]
    public void EachRow_IsTheServersFigures_AndOnlyTheTotalsAreAddedUp()
    {
        var reports = Setup();
        var cut = RenderComponent<BillingRegisterPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count), RenderTimeout);

        var rosa = cut.FindAll("tbody tr")[0].TextContent;
        Assert.Contains("Rosa Aquino", rosa);
        Assert.Contains("10,800.00", rosa);
        Assert.Contains("720.00", rosa);    // surcharge
        Assert.Contains("72.00", rosa);     // interest
        Assert.DoesNotContain("Other fines", cut.Markup);   // no account has one
        Assert.Contains("15,192.00", rosa);   // the statement's TotalDue: 10,800 + 3,600 + 792
        var nora = cut.FindAll("tbody tr")[1].TextContent;
        Assert.Contains("0012345", nora);
        Assert.Contains("4,800.00", nora);   // paid ahead, carried to later bills
        Assert.Contains("to Jan 2027", nora);
        Assert.Contains("Paid ahead", cut.Find("thead").TextContent);

        var totals = cut.Find("tfoot").TextContent;
        Assert.Contains("6,000.00", totals);   // billed this month
        Assert.Contains("15,192.00", totals);   // paid ahead is never taken off what is due now
        Assert.Contains("4,800.00", totals);

        Assert.Equal($"/profile/tcc/{Rosa}", cut.Find("tbody tr a").GetAttribute("href"));   // by id, never by number
        // The count / due date / late-charge rates lines are no longer on the sheet (2026-11-24).
        Assert.DoesNotContain("Rent due", cut.Markup);
        Assert.Empty(cut.FindAll(".br-rules"));
        Assert.Contains("Demo Trade Center", cut.Markup);
        reports.Verify(r => r.GetBillingRegisterAsync(FacilityCode.TCC, null, null), Times.Once);

        cut.FindAll("button").First(b => b.TextContent.Contains("Print")).Click();
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "stalltrackPrint.landscape");
    }

    [Fact]
    public void TheSummary_IsTheRegistersOwnRows_AndSwitchingViewReadsNothingAgain()
    {
        var reports = Setup();
        var cut = RenderComponent<BillingRegisterPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count), RenderTimeout);

        cut.FindAll(".br-view button").Single(b => b.TextContent == "Summary").Click();

        Assert.Empty(cut.FindAll("table"));
        var figures = cut.Find(".ss").TextContent;
        Assert.Contains("₱6,000.00", figures);    // billed: 3,600 + 2,400
        Assert.Contains("₱2,400.00", figures);    // collected
        Assert.Contains("40%", figures);
        Assert.Contains("₱15,192.00", figures);   // total due, as the register's totals row
        Assert.Contains("1 of 2", figures);       // accounts behind
        Assert.Contains("1 paid", cut.Find(".brs-counts").TextContent.Replace("\n", " "));

        var band = cut.FindAll(".brs-bars-split li").Single(li => li.TextContent.Contains("3–5 months")).TextContent;
        Assert.Contains("₱10,800.00", band);
        var largest = cut.Find("#brs-largest").ParentElement!.QuerySelectorAll("ol a");
        Assert.Equal(1, largest.Length);   // a settled account has no balance to rank
        Assert.Equal($"/profile/tcc/{Rosa}", largest[0].GetAttribute("href"));

        Assert.EndsWith("view=summary", Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>().Uri);
        reports.Verify(r => r.GetBillingRegisterAsync(FacilityCode.TCC, null, null), Times.Once);

        cut.FindAll("button").First(b => b.TextContent.Contains("Print")).Click();
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "stalltrackPrint.reportDocument");
    }

    [Fact]
    public void ThePreviousMonth_IsAskedOfTheServer_AndNextStopsAtThisMonth()
    {
        var reports = Setup();
        var cut = RenderComponent<BillingRegisterPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count), RenderTimeout);
        var today = PhilippineTime.Today;
        var before = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        Assert.True(cut.Find("[aria-label='Next month']").HasAttribute("disabled"));   // no month after this one
        Assert.Empty(cut.FindAll(".br-past-note"));

        cut.Find("[aria-label='Previous month']").Click();

        cut.WaitForAssertion(() => reports.Verify(r => r.GetBillingRegisterAsync(FacilityCode.TCC, before.Year, before.Month), Times.Once), RenderTimeout);
        Assert.Contains(before.ToString("MMMM yyyy"), cut.Find(".br-period").TextContent);
        Assert.False(cut.Find("[aria-label='Next month']").HasAttribute("disabled"));
        Assert.EndsWith($"month={before:yyyy-MM}", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void AnUnreadFacilityList_SaysSo_InsteadOfGuessingOne()
    {
        var reports = Setup(catalogRead: false);
        var cut = RenderComponent<BillingRegisterPage>();

        cut.WaitForAssertion(() => Assert.Contains("facilities could not be read", cut.Find("[role=alert]").TextContent), RenderTimeout);
        Assert.Empty(cut.FindAll("table"));
        reports.Verify(r => r.GetBillingRegisterAsync(It.IsAny<FacilityCode>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }
}
