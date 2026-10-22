using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The compact rental action bar under a profile header, and the drawer it opens. It draws only what the server allows, in the same row for a stall and a
/// monthly space, and confirms with one operation id per intent so a retry can never apply an action twice.
/// </summary>
public sealed class AccountLifecycleBarTests : TestContext
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly Mock<ILifecycleApiClient> _api = new();

    public AccountLifecycleBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
    }

    private static AccountLifecycleDto View(params AllowedAction[] actions) => new(LifecycleSource.SpaceRental, AccountId, "12", "KanmanggaySpaceRental", "Josephine",
        LifecycleState.Closed, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), null, actions, [new(new DateOnly(2026, 6, 30), "Closed", "Account closed")]);

    private static AllowedAction Offered(LifecycleAction a) => new(a, true, null, null, "Admin", true);
    private static AllowedAction Blocked(LifecycleAction a, string code, string text) => new(a, false, code, text, "Admin", true);

    private IRenderedComponent<AccountLifecycleBar> Render(AccountLifecycleDto view, Func<Guid, string>? profileHref = null, Action<AccountLifecycleDto>? loaded = null)
    {
        _api.Setup(a => a.GetAsync(LifecycleSource.SpaceRental, AccountId)).ReturnsAsync(Result<AccountLifecycleDto>.Success(view));
        return RenderComponent<AccountLifecycleBar>(p => p.Add(x => x.Source, LifecycleSource.SpaceRental).Add(x => x.AccountId, AccountId)
            .Add(x => x.ProfileHref, profileHref).Add(x => x.OnLoaded, v => loaded?.Invoke(v)));
    }

    private static LifecyclePreviewDto Ready(string version = "v1") => new(LifecycleAction.Remove, true, null, null, version,
        new Dictionary<string, string> { ["Space"] = "12", ["Holder"] = "Josephine" });

    private void OpenRetire(IRenderedComponent<AccountLifecycleBar> cut)
    {
        cut.Find(".lcb-bar button").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#lc-reason")), Wait);
    }

    private void Review(IRenderedComponent<AccountLifecycleBar> cut, string confirmText)
    {
        cut.Find("#lc-reason").Input("Space removed from the roll");
        cut.Find("#lc-form").Submit();
        cut.WaitForAssertion(() => Assert.Contains(confirmText, cut.Markup), Wait);
    }

    [Fact]
    public void DrawsOnlyTheActionsTheServerAllows_NoExplanations_AndNothingAtAllWhenThereAreNone()
    {
        var cut = Render(View(Offered(LifecycleAction.Resume), Offered(LifecycleAction.Reassign),
            Blocked(LifecycleAction.Renew, LifecycleReason.AccountClosed, "A closed account is resumed, not renewed."),
            Blocked(LifecycleAction.Remove, LifecycleReason.RoleRequired, "Only the Head may do this.")));

        Assert.Equal(["Resume account", "Reassign holder"], cut.FindAll(".lcb-bar button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.DoesNotContain("closed account is resumed", cut.Markup);                          // no explanation text for actions that do not apply
        Assert.DoesNotContain("Only the Head", cut.Markup);
        Assert.DoesNotContain("11111111-1111", cut.Markup);                                      // no raw ids

        var none = Render(View(Blocked(LifecycleAction.Resume, LifecycleReason.NotClosed, "Only a closed account can be resumed.")));
        Assert.Empty(none.FindAll(".lcb-bar"));
    }

    [Fact]
    public void AnActionWaitingOnTheOneTimeSpaceSetup_IsShownDisabled_WithAShortTooltip()
    {
        var cut = Render(View(Blocked(LifecycleAction.Resume, "NoSpaceRegistration", "This account needs its one-time space setup first.")));
        var button = cut.Find(".lcb-bar button");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("This account needs its one-time space setup first.", button.GetAttribute("title"));
    }

    [Fact]
    public void TheHostReceivesTheServersView_ForItsOwnPillAndHistory()
    {
        AccountLifecycleDto? seen = null;
        Render(View(Offered(LifecycleAction.Resume)), loaded: v => seen = v);
        Assert.Equal(("Josephine", LifecycleState.Closed), (seen!.Holder, seen.State));
    }

    [Fact]
    public void RetireNeedsAReason_ThenReviewsAndConfirmsOnce_WithThePreviewedVersion_AndTheBarRefreshes()
    {
        _api.Setup(a => a.PreviewAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleCommand>())).ReturnsAsync(Result<LifecyclePreviewDto>.Success(Ready("v7")));
        LifecycleExecution? sent = null;
        _api.Setup(a => a.ExecuteAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleExecution>()))
            .Callback<LifecycleSource, Guid, LifecycleExecution>((_, _, e) => sent = e)
            .ReturnsAsync(Result<LifecycleOutcome>.Success(new(Guid.NewGuid(), AccountId, null, LifecycleAction.Remove, false)));
        var cut = Render(View(Offered(LifecycleAction.Remove)));

        OpenRetire(cut);
        cut.Find("#lc-form").Submit();                                                            // no reason yet: stays on the form, error beside the field
        Assert.Equal("Give a reason.", cut.Find("#lc-reason-err").TextContent);
        _api.Verify(a => a.ExecuteAsync(It.IsAny<LifecycleSource>(), It.IsAny<Guid>(), It.IsAny<LifecycleExecution>()), Times.Never);

        Review(cut, "Confirm: Retire account");
        cut.Find("#lc-form").Submit();                                                            // confirm
        cut.WaitForAssertion(() => Assert.NotNull(sent), Wait);
        Assert.Equal(("v7", LifecycleAction.Remove), (sent!.ExpectedVersion, sent.Command.Action));
        Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#lc-form")), Wait);                  // the drawer closes
        _api.Verify(a => a.ExecuteAsync(It.IsAny<LifecycleSource>(), It.IsAny<Guid>(), It.IsAny<LifecycleExecution>()), Times.Once);
        _api.Verify(a => a.GetAsync(LifecycleSource.SpaceRental, AccountId), Times.AtLeast(2));   // the account is read again
    }

    [Fact]
    public void ARetryAfterNoAnswer_ReusesTheSameOperationId_SoItCanNeverApplyTwice()
    {
        _api.Setup(a => a.PreviewAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleCommand>())).ReturnsAsync(Result<LifecyclePreviewDto>.Success(Ready()));
        _api.SetupSequence(a => a.ExecuteAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleExecution>()))
            .ThrowsAsync(new HttpRequestException("timeout"))
            .ReturnsAsync(Result<LifecycleOutcome>.Success(new(Guid.NewGuid(), AccountId, null, LifecycleAction.Remove, true)));
        var cut = Render(View(Offered(LifecycleAction.Remove)));

        OpenRetire(cut);
        Review(cut, "Confirm: Retire account");
        cut.Find("#lc-form").Submit();                                                            // first confirmation: no answer
        cut.WaitForAssertion(() => Assert.Contains("never applied twice", cut.Markup), Wait);
        cut.Find("#lc-form").Submit();                                                            // retry of the same intent
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#lc-form")), Wait);
        var ids = _api.Invocations.Where(i => i.Method.Name == nameof(ILifecycleApiClient.ExecuteAsync)).Select(i => ((LifecycleExecution)i.Arguments[2]).ClientOperationId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public void AStalePreview_SendsThePersonBackToReview_AndAForbiddenConfirmationIsExplained()
    {
        _api.Setup(a => a.PreviewAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleCommand>())).ReturnsAsync(Result<LifecyclePreviewDto>.Success(Ready()));
        _api.SetupSequence(a => a.ExecuteAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleExecution>()))
            .ReturnsAsync(Result<LifecycleOutcome>.Failure("StalePreview: changed.", ResultStatus.Conflict))
            .ReturnsAsync(Result<LifecycleOutcome>.Forbidden());
        var cut = Render(View(Offered(LifecycleAction.Remove)));

        OpenRetire(cut);
        Review(cut, "Confirm: Retire account");
        cut.Find("#lc-form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("changed after it was reviewed", cut.Find(".avm-error").TextContent), Wait);
        Assert.Contains("Review", cut.Find("button[type='submit']").TextContent);                 // back to the form: nothing was applied

        cut.Find("#lc-form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Confirm: Retire account", cut.Markup), Wait);
        cut.Find("#lc-form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("not allowed", cut.Find(".avm-error").TextContent), Wait);
    }

    [Fact]
    public void ABalanceStillOwed_BlocksRetire_WithTheServersOwnWords()
    {
        _api.Setup(a => a.PreviewAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleCommand>()))
            .ReturnsAsync(Result<LifecyclePreviewDto>.Success(new(LifecycleAction.Remove, false, SpaceLifecycleWorkflow.OutstandingBalance, "₱7,200.00 is still owed on this account. Collect or settle it before retiring.", "v", new Dictionary<string, string>())));
        var cut = Render(View(Offered(LifecycleAction.Remove)));

        cut.Find(".lcb-bar button").Click();
        cut.WaitForAssertion(() => Assert.Contains("₱7,200.00 is still owed", cut.Find(".lc-blocked").TextContent), Wait);
        Assert.Empty(cut.FindAll("#lc-reason"));
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
    }

    [Fact]
    public void AStallsActionsAreHandedToTheHost_UnderTheStallsOwnWords_AndNoSpaceDrawerOpens()
    {
        var stallId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var view = new AccountLifecycleDto(LifecycleSource.Stall, stallId, "5", "NPM", "Maria Santos", LifecycleState.Closed, new DateOnly(2023, 6, 1), new DateOnly(2026, 5, 1), null,
            [new(LifecycleAction.Resume, true, null, null, "Admin", true), new(LifecycleAction.Reassign, true, null, null, "Admin", true),
             new(LifecycleAction.Close, false, "AlreadyClosed", "Already closed.", "SuperAdmin", true)], []);
        _api.Setup(a => a.GetAsync(LifecycleSource.Stall, stallId)).ReturnsAsync(Result<AccountLifecycleDto>.Success(view));
        var chosen = new List<LifecycleAction>();
        var cut = RenderComponent<AccountLifecycleBar>(p => p.Add(x => x.Source, LifecycleSource.Stall).Add(x => x.AccountId, stallId).Add(x => x.OnStallAction, a => chosen.Add(a)));

        Assert.Equal(["Resume account", "Assign new stall"], cut.FindAll(".lcb-bar button").Select(b => b.TextContent.Trim()).ToArray());   // a stall's Reassign is "Assign new stall"
        Assert.DoesNotContain("Close account", cut.Markup);
        cut.FindAll(".lcb-bar button")[1].Click();
        Assert.Equal([LifecycleAction.Reassign], chosen);
        Assert.Empty(cut.FindAll("#lc-form"));
    }

    [Fact]
    public void ResumeOpensTheNewOccupancy_NotTheClosedPredecessor_WithTheServersEarliestStartPrefilled()
    {
        var successor = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var facts = new Dictionary<string, string> { ["Earliest start"] = "Jul 1, 2026", ["Approved monthly amount"] = "₱1,200.00" };
        _api.Setup(a => a.PreviewAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleCommand>()))
            .ReturnsAsync((LifecycleSource _, Guid _, LifecycleCommand c) => Result<LifecyclePreviewDto>.Success(
                new(LifecycleAction.Resume, c.EffectiveDate is not null, c.EffectiveDate is null ? "StartRequired" : null, null, "v1", facts)));
        _api.Setup(a => a.ExecuteAsync(LifecycleSource.SpaceRental, AccountId, It.IsAny<LifecycleExecution>()))
            .ReturnsAsync(Result<LifecycleOutcome>.Success(new(Guid.NewGuid(), AccountId, successor, LifecycleAction.Resume, false)));
        var cut = Render(View(Offered(LifecycleAction.Resume)), id => $"/operations/kanmanggay/profile/{id}");

        cut.WaitForElement(".lcb-bar button", Wait).Click();
        cut.WaitForAssertion(() => Assert.Equal("2026-07-01", cut.Find("#lc-date").GetAttribute("value")), Wait);   // the server's earliest start, not a guess
        cut.Find("#lc-reason").Input("Same holder returns");
        cut.Find("#lc-form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Confirm: Resume account", cut.Markup), Wait);
        cut.Find("#lc-form").Submit();
        cut.WaitForAssertion(() => Assert.EndsWith($"/operations/kanmanggay/profile/{successor}", Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>().Uri), Wait);
    }
}
