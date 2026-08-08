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

    /// <summary>Renders the register with several accounts, for the summary figures rather than the renew dialog.</summary>
    private IRenderedComponent<ClosedAccounts> Render(params ClosedStallAccountDto[] accounts)
    {
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(a => a.GetClosedStallAccountsAsync())
            .ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success(accounts));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<ClosedAccounts>();
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

    [Fact]
    public void EndedAndLapsedBalances_AreNeverAddedIntoOneTotal()
    {
        // Two accounts on the register: one handed over (finished — this register is the only statement of its
        // balance) and one merely lapsed (the tenant is still there, so the arrears and follow-up lists already
        // state its balance in full). A single "Total Uncollected" of ₱65,730 let a head add this document to the
        // follow-up total and double the municipality's receivables.
        var handedOver = ExpiredAccount() with
        {
            StallId = Guid.NewGuid(),
            StallNo = "23",
            State = InactiveAccountState.Superseded,
            Uncollected = 32_430m,
            StallReLet = true,
        };
        var lapsed = ExpiredAccount() with
        {
            StallId = Guid.NewGuid(),
            StallNo = "7",
            State = InactiveAccountState.Lapsed,
            Uncollected = 33_300m,
        };

        var cut = Render(handedOver, lapsed);

        cut.WaitForAssertion(() =>
        {
            // Each figure is stated under its own heading with its own count, and the mixed sum appears nowhere.
            Assert.Contains("Stated here only", cut.Markup);
            Assert.Contains("Lapsed · already in follow-up", cut.Markup);
            Assert.Contains("32,430", cut.Markup);
            Assert.Contains("33,300", cut.Markup);
            Assert.DoesNotContain("65,730", cut.Markup);
            Assert.DoesNotContain("Total Uncollected", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public void AClosedAccountNobodyElseHolds_OffersResume_NotAssignNewStall()
    {
        // A space let without a contract, closed the same day it was recorded, and nobody else in it. The register
        // decided such a stall had been re-let — counting the account's own occupancy as its successor — and offered
        // "Assign new stall" as the only action: the office was told the space was taken by the lessee it had just
        // closed, with no way to put her back and no way to remove the account.
        var closed = ExpiredAccount() with
        {
            State = InactiveAccountState.Closed,
            FacilityCode = FacilityCode.TCC,
            FacilityName = "Tampak Commercial Center",
            StallNo = "4",
            Occupant = "Bernadette Lim",
            ContractName = null,
            DurationYears = 0,
            MonthlyRate = 1_500m,
            Uncollected = 1_500m,
            ClosedOn = new DateOnly(2026, 8, 8),
            ClosedBy = "head",
            Section = "",
            StallReLet = false,
        };

        // The array picks the plain render overload; the single-row one waits for a renew button this row will not have.
        var cut = Render(new[] { closed });

        cut.WaitForAssertion(() =>
        {
            var resume = cut.Find(".ca-row-reopen");
            // Icon-only controls name nothing to a screen reader on their own, and this is a government record.
            Assert.Equal("Resume account", resume.GetAttribute("aria-label"));
            Assert.DoesNotContain("Assign new stall", cut.Markup);
        }, RenderTimeout);
    }
}
