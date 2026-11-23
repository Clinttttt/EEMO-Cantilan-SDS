using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Rent paid for months not yet begun is carried to those months' bills (panel, carry forward): the statement states it and never takes it off what is due now.</summary>
public class AccountSummaryPaidAheadTests : TestContext
{
    [Fact]
    public void MoneyPaidAhead_IsStatedAfterTheTotal_AndNeverTakenOffIt()
    {
        var facilities = Mock.Of<IFacilitiesApiClient>();
        Services.AddSingleton(facilities);
        Services.AddSingleton(new FacilityState(facilities));
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton(Mock.Of<IReportsApiClient>());
        Services.AddSingleton<BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // November is paid; December in full and January in part are paid ahead.
        var statement = new StallStatementDto(new(2026, 11, 24), 0, 0m, 0, 0m, 1_000m, 1_000m, 0m, [], [],
            PaidAheadMonths: 2, PaidAhead: 1_400m, PaidAheadThrough: new DateOnly(2027, 1, 1));
        var cut = RenderComponent<AccountSummary>(p => p.Add(x => x.Statement, statement));

        Assert.Contains(cut.FindAll(".acct-row"), r => r.TextContent.Contains("Total amount due") && r.TextContent.Contains("₱0.00"));
        var ahead = cut.Find(".acct-row.ahead").TextContent;
        Assert.Contains("Paid ahead", ahead);
        Assert.Contains("2 months, to January 2027", ahead);
        Assert.Contains("₱1,400.00", ahead);

        cut.SetParametersAndRender(p => p.Add(x => x.Statement, statement with { PaidAheadMonths = 0, PaidAhead = 0m, PaidAheadThrough = null }));
        Assert.Empty(cut.FindAll(".acct-row.ahead"));
    }
}
