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
    private static ClosedStallAccountDto ExpiredAccount(NpmMonthBasis monthBasis = NpmMonthBasis.RentGoal) => new(
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
        AreaNote: "Extension",
        MonthBasis: monthBasis);

    /// <summary>Renders the register with several accounts, for the summary figures rather than the renew dialog.</summary>
    private IRenderedComponent<ClosedAccounts> Render(params ClosedStallAccountDto[] accounts)
    {
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(a => a.GetClosedStallAccountsAsync())
            .ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success(accounts));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(stalls.Object);
        var obligations = new Mock<IObligationsApiClient>();
        obligations.Setup(x => x.GetWorkspaceAsync(It.IsAny<ObligationKind>())).ReturnsAsync(Result<EEMOCantilanSDS.Application.Dtos.Revenue.ObligationWorkspaceDto>.Success(new([], 0m, 0m, 0m)));
        Services.AddSingleton(obligations.Object);
        Services.AddSingleton(Mock.Of<ILifecycleApiClient>());
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
    [Fact]
    public void StallRowActionsAreLinksToTheStallsOwnProfile_AndTheReportOpensNoDialogAndCallsNothing()
    {
        var closed = ExpiredAccount() with { State = InactiveAccountState.Closed, StallNo = "4", ClosedOn = new DateOnly(2026, 5, 1) };
        var expired = ExpiredAccount() with { StallNo = "5" };
        var reLet = ExpiredAccount() with { State = InactiveAccountState.Superseded, StallNo = "6", StallReLet = true };
        var cut = Render(closed, expired, reLet);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr[data-account], .ca-act-icons").Count > 0 ? cut.FindAll(".ca-row-reopen, .ca-row-renew").Count : 0), RenderTimeout);
        string Href(string css, ClosedStallAccountDto row) =>
            cut.FindAll(css).Single(e => e.GetAttribute("href")!.Contains(row.StallId.ToString())).GetAttribute("href")!;
        Assert.Equal($"/profile/npm/{closed.StallId}?action=resume&contract={closed.ContractId}", Href(".ca-row-reopen", closed));
        Assert.Equal($"/profile/npm/{expired.StallId}?action=renew&contract={expired.ContractId}", Href(".ca-row-renew", expired));
        Assert.Equal($"/profile/npm/{reLet.StallId}?action=assign&contract={reLet.ContractId}", Href(".ca-row-renew", reLet));
        Assert.Empty(cut.FindAll(".ca-row-icon:is(button).ca-row-reopen, button.ca-row-renew, button.ca-row-remove"));   // links, not mutations
        Assert.Empty(cut.FindAll(".eemo-modal"));                                                                          // the report hosts no dialog
    }
}
