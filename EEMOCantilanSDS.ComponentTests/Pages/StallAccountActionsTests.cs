using Bunit;
using EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The stall account dialogs (Resume, Renew, Assign new stall, Remove), now hosted by the stall's own profile. They are the dialogs the Closed Accounts
/// register used to open, unchanged: the same Stalls endpoints with the same arguments, and a renewal states only what the clerk actually changed.
/// The renewal tests came with them: the register states a DERIVED rent, so an untouched box must never be sent back as a correction.
/// </summary>
public sealed class StallAccountActionsTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);
    private readonly Mock<IStallsApiClient> _stalls = new();
    private readonly List<RenewStallContractRequest> _renewals = [];
    private int _done, _cancelled;

    public StallAccountActionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _stalls.Setup(a => a.RenewStallContractAsync(It.IsAny<Guid>(), It.IsAny<RenewStallContractRequest>()))
            .Callback<Guid, RenewStallContractRequest>((_, r) => _renewals.Add(r)).ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(_stalls.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
    }

    /// <summary>An expired account: term lapsed, stall still open, nobody else in it.</summary>
    private static ClosedStallAccountDto ExpiredAccount(NpmMonthBasis monthBasis = NpmMonthBasis.RentGoal) => new(
        StallId: Guid.NewGuid(), State: InactiveAccountState.Expired, FacilityCode: FacilityCode.NPM, FacilityName: "New Public Market", StallNo: "5",
        Occupant: "Maria Santos", ContractName: "Maria Santos", EffectivityDate: new DateOnly(2023, 6, 1), DurationYears: 3, MonthlyRate: 900m,
        ClosedOn: null, ExpiryDate: new DateOnly(2026, 6, 1), LifetimeCollected: 12_000m, Uncollected: 0m, ClosedBy: null, Section: "Vegetable Area",
        OccupancyEndedOn: new DateOnly(2026, 6, 1), StallReLet: false, ContractId: Guid.NewGuid(), AreaSqm: 4.0, AreaNote: "Extension", MonthBasis: monthBasis);

    private IRenderedComponent<StallAccountActions> Render(ClosedStallAccountDto row, StallAccountAction action) =>
        RenderComponent<StallAccountActions>(p => p.Add(x => x.Account, row).Add(x => x.Action, action)
            .Add(x => x.OnDone, () => _done++).Add(x => x.OnCancel, () => _cancelled++));

    // ── Renew ──

    [Fact]
    public void Proceed_StatesNoFigures_SoTheStallKeepsItsOwnRate()
    {
        var cut = Render(ExpiredAccount(), StallAccountAction.Renew);
        cut.WaitForElement(".eemo-modal-footer .btn-primary", RenderTimeout).Click();   // Proceed

        cut.WaitForAssertion(() => Assert.NotEmpty(_renewals), RenderTimeout);
        var req = Assert.Single(_renewals);
        Assert.Null(req.MonthlyRate);
        Assert.Null(req.AreaSqm);
        Assert.Null(req.AreaNote);
        Assert.Equal("Maria Santos", req.ActualOccupant);
        Assert.Equal(3, req.DurationYears);
        Assert.Equal(1, _done);
    }

    [Fact]
    public void Edit_WithNothingChanged_StillStatesNoFigures()
    {
        var cut = Render(ExpiredAccount(), StallAccountAction.Renew);
        cut.WaitForElement(".ca-renew-edit", RenderTimeout).Click();
        cut.Find(".eemo-modal-footer .btn-primary").Click();       // Confirm Renew

        var req = Assert.Single(_renewals);
        Assert.Null(req.MonthlyRate);
        Assert.Null(req.AreaSqm);
        Assert.Null(req.AreaNote);
    }

    [Fact]
    public void Edit_WithACorrectedRent_StatesOnlyThat()
    {
        var cut = Render(ExpiredAccount(), StallAccountAction.Renew);
        cut.WaitForElement(".ca-renew-edit", RenderTimeout).Click();
        cut.Find(".ca-renew-rate").Change("1800");
        cut.Find(".eemo-modal-footer .btn-primary").Click();

        var req = Assert.Single(_renewals);
        Assert.Equal(1800m, req.MonthlyRate);
        Assert.Null(req.AreaSqm);      // untouched, so not stated
        Assert.Null(req.AreaNote);
    }

    [Fact]
    public void Edit_ShowsTheRecordItIsReLetOn()
    {
        var cut = Render(ExpiredAccount(), StallAccountAction.Renew);
        cut.WaitForElement(".ca-renew-edit", RenderTimeout).Click();

        // The facts that identify the space are stated, not offered as fields.
        Assert.Contains("New Public Market", cut.Markup);
        Assert.Contains("Vegetable Area", cut.Markup);
        Assert.Contains("Jun 1, 2023", cut.Markup);
        Assert.Contains("Whole year: ₱10,800", cut.Markup);
    }

    [Fact]
    public void PureDaysRenewalShowsNoFixedMonthlyOrWholeYearRent_AndSendsNoDormantLegacyRate()
    {
        var cut = Render(ExpiredAccount(NpmMonthBasis.PureDays) with { MonthlyRate = 5_000m }, StallAccountAction.Renew);
        cut.WaitForElement(".ca-renew-edit", RenderTimeout).Click();

        Assert.Empty(cut.FindAll(".ca-renew-rate"));
        Assert.DoesNotContain("Whole year:", cut.Markup);
        Assert.Contains("Calendar-day billing", cut.Markup);
        cut.Find(".eemo-modal-footer .btn-primary").Click();
        Assert.Null(Assert.Single(_renewals).MonthlyRate);
    }

    [Fact]
    public void ARefusedRenewal_IsStatedInTheDialog_AndTheHostIsNotToldItIsDone()
    {
        _stalls.Setup(a => a.RenewStallContractAsync(It.IsAny<Guid>(), It.IsAny<RenewStallContractRequest>())).ReturnsAsync(Result<bool>.Failure("Refused by the server."));
        var cut = Render(ExpiredAccount(), StallAccountAction.Renew);
        cut.WaitForElement(".eemo-modal-footer .btn-primary", RenderTimeout).Click();
        cut.WaitForAssertion(() => Assert.Contains("Refused by the server.", cut.Find(".ca-form-err").TextContent), RenderTimeout);
        Assert.Equal(0, _done);
    }

    // ── Resume, Remove, Assign ──

    [Fact]
    public void Resume_PutsTheSameLesseeBack_ThroughTheExistingEndpoint_OnceAndOnlyOnce()
    {
        var row = ExpiredAccount() with { State = InactiveAccountState.Closed, ClosedOn = new DateOnly(2026, 5, 1) };
        _stalls.Setup(a => a.ToggleStallStatusAsync(row.StallId, false)).ReturnsAsync(Result<bool>.Success(true));
        var cut = Render(row, StallAccountAction.Resume);

        Assert.Contains("it will not be back-billed", cut.Markup.Replace("\n", " ").Replace("  ", " "));
        var confirm = cut.WaitForElement(".eemo-modal-footer .btn-primary", RenderTimeout);
        confirm.Click();
        cut.WaitForAssertion(() => Assert.Equal(1, _done), RenderTimeout);
        _stalls.Verify(a => a.ToggleStallStatusAsync(row.StallId, false), Times.Once);
    }

    [Fact]
    public void Remove_UsesTheExistingSoftDelete_AndNamesWhatItKeeps()
    {
        var row = ExpiredAccount() with { State = InactiveAccountState.Closed };
        _stalls.Setup(a => a.SoftDeleteStallAsync(row.StallId)).ReturnsAsync(Result<bool>.Success(true));
        var cut = Render(row, StallAccountAction.Remove);

        Assert.Contains("collection history is kept for audit", cut.Markup.Replace("\n", " ").Replace("  ", " "));
        cut.WaitForElement(".eemo-modal-footer .btn-danger", RenderTimeout).Click();
        cut.WaitForAssertion(() => Assert.Equal(1, _done), RenderTimeout);
        _stalls.Verify(a => a.SoftDeleteStallAsync(row.StallId), Times.Once);
    }

    [Fact]
    public void Assign_PlacesThePayorInANewStall_AndLeavesTheOldAccountAlone()
    {
        var row = ExpiredAccount() with { State = InactiveAccountState.Superseded, StallReLet = true, Uncollected = 4_200m };
        _stalls.Setup(a => a.GetStallReassignmentPreviewAsync(row.StallId, row.ContractId)).ReturnsAsync(Result<StallReassignmentPreviewDto>.Success(
            new(row.StallId, row.FacilityName, row.StallNo, "Vegetable Area", row.Occupant, row.ContractName, 900m, false, "12", 3, row.MonthBasis)));
        AssignPastOccupantStallCommand? sent = null;
        _stalls.Setup(a => a.AssignPastOccupantStallAsync(It.IsAny<AssignPastOccupantStallCommand>()))
            .Callback<AssignPastOccupantStallCommand>(c => sent = c).ReturnsAsync(Result<StallDto>.Success(default!));
        var cut = Render(row, StallAccountAction.Assign);

        cut.WaitForAssertion(() => Assert.Contains("₱4,200", cut.Markup), RenderTimeout);               // the old balance is stated, and stays
        cut.Find(".eemo-modal-footer .btn-primary").Click();
        cut.WaitForAssertion(() => Assert.NotNull(sent), RenderTimeout);
        Assert.Equal((row.StallId, "12", row.ContractId), (sent!.PreviousStallId, sent.StallNo, sent.ContractId));
        Assert.Equal(1, _done);
    }

    [Fact]
    public void Cancel_ClosesTheDialogWithoutCallingAnything()
    {
        var cut = Render(ExpiredAccount(), StallAccountAction.Renew);
        cut.WaitForElement(".eemo-modal-footer .btn-ghost", RenderTimeout).Click();
        Assert.Equal((1, 0), (_cancelled, _done));
        Assert.Empty(_renewals);
    }
}
