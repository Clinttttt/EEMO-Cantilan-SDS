using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using ClosedAccounts = EEMOCantilanSDS.Client.Components.Pages.Reports.ClosedAccounts;

/// <summary>
/// bUnit tests for the renewal dialog on the Closed / Inactive Accounts register.
///
/// These exist because of a real defect in the dialog's Edit view. The register states a DERIVED rent — for a
/// daily-collected space, thirty of its daily fee; for a monthly one, the rate the LAPSED term was let at, which
/// the repository itself documents as "never the hand-entered figure stored on the stall". The Edit form seeded
/// its rent box from that figure and always sent it back, so renewing an account and only fixing the occupant's
/// spelling silently rewrote the stall's stored monthly rate. Nothing may be sent unless the clerk changed it.
/// </summary>
public class ClosedAccountsRenewTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    /// <summary>An expired account: term lapsed, stall still open, nobody else in it.</summary>
    private static ClosedStallAccountDto ExpiredAccount() => new(
        StallId: Guid.NewGuid(),
        State: InactiveAccountState.Expired,
        FacilityCode: FacilityCode.NPM,
        FacilityName: "New Public Market",
        StallNo: "5",
        Occupant: "Maria Santos",
        ContractName: "Maria Santos",
        EffectivityDate: new DateOnly(2023, 6, 1),
        DurationYears: 3,
        MonthlyRate: 900m,                       // derived: 30 × the tenant's daily fee
        ClosedOn: null,
        ExpiryDate: new DateOnly(2026, 6, 1),
        LifetimeCollected: 12_000m,
        Uncollected: 0m,
        ClosedBy: null,
        Section: "Vegetable Area",
        OccupancyEndedOn: new DateOnly(2026, 6, 1),
        StallReLet: false,
        ContractId: Guid.NewGuid(),
        AreaSqm: 4.0,
        AreaNote: "Extension");

    private (IRenderedComponent<ClosedAccounts> Cut, List<RenewStallContractRequest> Sent) Render(ClosedStallAccountDto row)
    {
        var sent = new List<RenewStallContractRequest>();
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(a => a.GetClosedStallAccountsAsync())
            .ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success(new[] { row }));
        stalls.Setup(a => a.RenewStallContractAsync(It.IsAny<Guid>(), It.IsAny<RenewStallContractRequest>()))
            .Callback<Guid, RenewStallContractRequest>((_, r) => sent.Add(r))
            .ReturnsAsync(Result<bool>.Success(true));

        JSInterop.Mode = JSRuntimeMode.Loose;   // the page prints and scrolls via JS
        Services.AddSingleton(stalls.Object);
        // Injected into every component by _Imports.razor, plus the page's own facility catalogue.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());   // the print sheet's SignatureStrip
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        var cut = RenderComponent<ClosedAccounts>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ca-row-renew")), RenderTimeout);
        return (cut, sent);
    }

    [Fact]
    public void Proceed_StatesNoFigures_SoTheStallKeepsItsOwnRate()
    {
        var (cut, sent) = Render(ExpiredAccount());

        cut.Find(".ca-row-renew").Click();
        cut.Find(".eemo-modal-footer .btn-primary").Click();   // Proceed

        var req = Assert.Single(sent);
        Assert.Null(req.MonthlyRate);
        Assert.Null(req.AreaSqm);
        Assert.Null(req.AreaNote);
        Assert.Equal("Maria Santos", req.ActualOccupant);
        Assert.Equal(3, req.DurationYears);
    }

    [Fact]
    public void Edit_WithNothingChanged_StillStatesNoFigures()
    {
        // The regression: opening Edit used to send the register's derived rent back as a correction.
        var (cut, sent) = Render(ExpiredAccount());

        cut.Find(".ca-row-renew").Click();
        cut.FindAll(".eemo-modal-footer .btn-ghost")[1].Click();   // Edit
        cut.Find(".eemo-modal-footer .btn-primary").Click();       // Confirm Renew

        var req = Assert.Single(sent);
        Assert.Null(req.MonthlyRate);
        Assert.Null(req.AreaSqm);
        Assert.Null(req.AreaNote);
    }

    [Fact]
    public void Edit_WithACorrectedRent_StatesOnlyThat()
    {
        var (cut, sent) = Render(ExpiredAccount());

        cut.Find(".ca-row-renew").Click();
        cut.FindAll(".eemo-modal-footer .btn-ghost")[1].Click();   // Edit
        cut.Find(".ca-renew-rate").Change("1800");
        cut.Find(".eemo-modal-footer .btn-primary").Click();

        var req = Assert.Single(sent);
        Assert.Equal(1800m, req.MonthlyRate);
        Assert.Null(req.AreaSqm);      // untouched, so not stated
        Assert.Null(req.AreaNote);
    }

    [Fact]
    public void Edit_ShowsTheRecordItIsReLetOn()
    {
        var (cut, _) = Render(ExpiredAccount());

        cut.Find(".ca-row-renew").Click();
        cut.FindAll(".eemo-modal-footer .btn-ghost")[1].Click();   // Edit

        // The facts that identify the space are stated, not offered as fields.
        Assert.Contains("New Public Market", cut.Markup);
        Assert.Contains("Vegetable Area", cut.Markup);
        Assert.Contains("Jun 1, 2023", cut.Markup);
        Assert.Contains("Whole year: ₱10,800", cut.Markup);
    }
}
