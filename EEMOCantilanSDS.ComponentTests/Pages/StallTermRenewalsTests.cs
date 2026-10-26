using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Renewals = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.StallTermRenewals;

/// <summary>
/// "Renew expired terms": the facility-owned bulk renewal that used to be a button in the Closed Accounts report. Same Stalls endpoint, same eligibility, one
/// account at a time with its own result; it touches only this facility's eligible accounts and never an account without a term.
/// </summary>
public sealed class StallTermRenewalsTests : TestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly Mock<IStallsApiClient> _stalls = new();
    private readonly List<(Guid Id, RenewStallContractRequest Request)> _renewed = [];
    private readonly HashSet<Guid> _refuse = [];

    public StallTermRenewalsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Admin");
        _stalls.Setup(a => a.RenewStallContractAsync(It.IsAny<Guid>(), It.IsAny<RenewStallContractRequest>()))
            .ReturnsAsync((Guid id, RenewStallContractRequest r) => { _renewed.Add((id, r)); return _refuse.Contains(id) ? Result<bool>.Failure("Refused by the server.") : Result<bool>.Success(true); });
        Services.AddSingleton(_stalls.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton<FacilityState>(new FacilityState(Mock.Of<IFacilitiesApiClient>()));
    }

    private static ClosedStallAccountDto Row(FacilityCode code, string name, InactiveAccountState state, string holder, string no, bool reLet = false, int years = 3) => new(
        StallId: Guid.NewGuid(), State: state, FacilityCode: code, FacilityName: name, StallNo: no, Occupant: holder, ContractName: holder,
        EffectivityDate: new DateOnly(2023, 6, 1), DurationYears: years, MonthlyRate: 900m, ClosedOn: state == InactiveAccountState.Closed ? new DateOnly(2026, 5, 1) : null,
        ExpiryDate: new DateOnly(2026, 6, 1), LifetimeCollected: 12_000m, Uncollected: 0m, ClosedBy: null, Section: "", OccupancyEndedOn: new DateOnly(2026, 6, 1),
        StallReLet: reLet, ContractId: Guid.NewGuid(), AreaSqm: null, AreaNote: null, MonthBasis: NpmMonthBasis.RentGoal);

    private IRenderedComponent<Renewals> Open(string slug, params ClosedStallAccountDto[] rows)
    {
        _stalls.Setup(a => a.GetClosedStallAccountsAsync()).ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success(rows));
        return RenderComponent<Renewals>(p => p.Add(x => x.Slug, slug));
    }

    [Fact]
    public void ListsOnlyThisFacilitysEligibleAccounts_NeverAnotherFacilitiesNorOnesThatCannotBeRenewed()
    {
        var ice1 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Lapsed, "Ice B", "2");
        var closed = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Closed, "Ice Closed", "3");
        var reLet = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Lapsed, "Ice Relet", "4", reLet: true);
        var renewed = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Renewed, "Ice Renewed", "5");
        var noTerm = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Space Only", "6", years: 0);     // a no-contract occupancy has no term to renew
        var npm = Row(FacilityCode.NPM, "New Public Market", InactiveAccountState.Expired, "Npm A", "9");
        var cut = Open("ice", ice1, ice2, closed, reLet, renewed, noTerm, npm);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".str-table tbody tr").Count), Wait);
        var text = cut.Find(".str-table").TextContent;
        Assert.Contains("Ice A", text); Assert.Contains("Ice B", text);
        foreach (var other in new[] { "Ice Closed", "Ice Relet", "Ice Renewed", "Space Only", "Npm A" }) Assert.DoesNotContain(other, text);
        Assert.Contains("Renew 2", cut.Find(".str-form button").TextContent);
    }

    [Fact]
    public void Renew_AppliesOnlyTheCommonStartAndTerm_EachAccountKeepsItsOwnHolderAndContractName_OnceEach()
    {
        var ice1 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice B", "2");
        var npm = Row(FacilityCode.NPM, "New Public Market", InactiveAccountState.Expired, "Npm A", "9");
        var cut = Open("ice", ice1, ice2, npm);
        cut.WaitForElement(".str-form button", Wait);
        cut.Find("#str-years").Change("5");
        cut.Find(".str-form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(2, _renewed.Count), Wait);
        Assert.Equal([ice1.StallId, ice2.StallId], _renewed.Select(r => r.Id).OrderBy(id => id == ice1.StallId ? 0 : 1).ToArray());
        Assert.DoesNotContain(npm.StallId, _renewed.Select(r => r.Id));
        Assert.All(_renewed, r => { Assert.Equal(5, r.Request.DurationYears); Assert.Null(r.Request.MonthlyRate); Assert.Null(r.Request.AreaSqm); });   // no figure is rewritten
        Assert.Equal(["Ice A", "Ice B"], _renewed.Select(r => r.Request.ActualOccupant).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void APartialFailure_IsReportedPerAccount_AndOnlyTheFailedOneRemainsToRetry()
    {
        var ice1 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice B", "2");
        _refuse.Add(ice2.StallId);
        var cut = Open("ice", ice1, ice2);
        cut.WaitForElement(".str-form button", Wait);
        // After the first pass the server no longer lists the account that renewed.
        _stalls.Setup(a => a.GetClosedStallAccountsAsync()).ReturnsAsync(() => Result<IReadOnlyList<ClosedStallAccountDto>>.Success(_renewed.Any(r => r.Id == ice1.StallId) ? [ice2] : [ice1, ice2]));
        cut.Find(".str-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("1 renewed, 1 failed", cut.Find(".str-message").TextContent), Wait);
        Assert.Equal(2, _renewed.Count);
        var rows = cut.FindAll(".str-table tbody tr");
        Assert.Single(rows);                                                                       // the renewed account dropped off; the refused one stays
        Assert.Contains("Ice B", rows[0].TextContent);
        Assert.Contains("Refused by the server.", rows[0].TextContent);
        Assert.Contains("Renew 1", cut.Find(".str-form button").TextContent);
    }

    [Fact]
    public void ThePencilCorrectsOneAccountsHolderAndContractName_AndOnlyThatRenewalCarriesIt()
    {
        var ice1 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice B", "2");
        var cut = Open("ice", ice1, ice2);
        cut.WaitForElement(".str-pencil", Wait);

        cut.FindAll(".str-pencil")[0].Click();
        cut.Find("#str-edit-holder").Input("Ice A. Santos");
        cut.Find("#str-edit-contract").Input("");
        Assert.True(cut.Find(".str-form button").HasAttribute("disabled"));                       // no renewal while an edit is open
        cut.FindAll(".str-edit-actions button")[0].Click();

        Assert.Contains("Ice A. Santos", cut.Find(".str-table").TextContent);
        cut.Find(".str-form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(2, _renewed.Count), Wait);
        var first = _renewed.Single(r => r.Id == ice1.StallId).Request;
        Assert.Equal("Ice A. Santos", first.ActualOccupant);
        Assert.Null(first.NameOnContract);
        Assert.Equal("Ice B", _renewed.Single(r => r.Id == ice2.StallId).Request.ActualOccupant);
    }

    [Fact]
    public void AnAccountLeftOutIsNotRenewed_StaysListedToPutBack_AndIsNotChanged()
    {
        var ice1 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = Row(FacilityCode.ICE, "Iceplant", InactiveAccountState.Expired, "Ice B", "2");
        var cut = Open("ice", ice1, ice2);
        cut.WaitForElement(".str-form button", Wait);

        cut.Find($"button[aria-label='Leave out Stall 2'], button[aria-label^='Leave out'][aria-label$=' 2']").Click();
        Assert.Contains("Renew 1 term", cut.Find(".str-form button").TextContent);
        Assert.Contains("Left out", cut.FindAll(".str-table tbody tr")[1].TextContent);
        Assert.Equal(2, cut.FindAll(".str-table tbody tr").Count);                                         // still listed

        cut.Find(".str-form").Submit();
        cut.WaitForAssertion(() => Assert.Single(_renewed), Wait);
        Assert.Equal(ice1.StallId, _renewed[0].Id);                                                         // the left-out account is never sent
    }

    [Fact]
    public void AnUnknownFacilityIsStated_NotAnEmptyList()
    {
        var cut = Open("nonsense");
        cut.WaitForAssertion(() => Assert.Contains("facility was not found", cut.Markup), Wait);
    }
}
