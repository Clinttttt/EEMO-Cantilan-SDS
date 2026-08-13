using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using TwoFactorPanel = EEMOCantilanSDS.Client.Components.Pages.Shared.TwoFactorPanel;

/// <summary>
/// bUnit render tests for the two-factor panel.
///
/// These exist because of a real defect: the panel's markup deliberately shows NO password field when
/// two-factor is off (the user is already signed in, and switching it on only adds protection), yet the
/// click handler still demanded a password. Every attempt to enable therefore failed with
/// "Enter your current password." against a field that did not exist, and nothing in the build or the
/// handler tests could catch it — the contradiction lived entirely between a component's markup and its
/// own code-behind.
/// </summary>
public class TwoFactorPanelTests : TestContext
{
    /// <summary>Generous, like ReportPageTests: the panel renders only after its async status load.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    private static MfaStatusDto Off() => new(Enabled: false, PendingEnrollment: false, EnrolledAt: null, RecoveryCodesRemaining: 0);

    private (IRenderedComponent<TwoFactorPanel> Cut, Mock<IMfaApiClient> Api) RenderPanel(MfaStatusDto status)
    {
        var api = new Mock<IMfaApiClient>();
        api.Setup(a => a.GetMfaStatusAsync()).ReturnsAsync(Result<MfaStatusDto>.Success(status));
        api.Setup(a => a.BeginMfaEnrollmentAsync(It.IsAny<BeginMfaEnrollmentCommand>()))
           .ReturnsAsync(Result<MfaEnrollmentDto>.Success(
               new MfaEnrollmentDto(
                   ManualKey: "JBSWY3DPEHPK3PXP",
                   ProvisioningUri: "otpauth://totp/EEMO%20Cantilan:head?secret=JBSWY3DPEHPK3PXP",
                   QrCodeDataUri: "data:image/png;base64,iVBORw0KGgo=")));

        Services.AddSingleton(api.Object);
        // The global _Imports.razor injects these into every component; stub them so the panel resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Head");

        return (RenderComponent<TwoFactorPanel>(p => p.Add(c => c.Flat, true)), api);
    }

    [Fact]
    public void Enabling_DoesNotAskForThePassword_AndStartsEnrolment()
    {
        var (cut, api) = RenderPanel(Off());

        cut.WaitForAssertion(() => Assert.Contains("Set up two-factor", cut.Markup), RenderTimeout);
        cut.Find("button.mfa-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            // The regression: this message must never appear on the enable path.
            Assert.DoesNotContain("Enter your current password.", cut.Markup);
            // Enrolment actually began — the setup key and QR are on screen.
            Assert.Contains("JBSWY3DPEHPK3PXP", cut.Markup);
        }, RenderTimeout);

        api.Verify(a => a.BeginMfaEnrollmentAsync(It.IsAny<BeginMfaEnrollmentCommand>()), Times.Once);
    }

    [Fact]
    public void EnablePath_RendersNoPasswordField()
    {
        var (cut, _) = RenderPanel(Off());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Set up two-factor", cut.Markup);
            // No password input exists here, which is precisely why the handler must not require one.
            Assert.Empty(cut.FindAll("input[type=password]"));
        }, RenderTimeout);
    }

    [Fact]
    public void UnfinishedSetup_IsStillAnnounced_WhenTheHostSuppressesTheIntro()
    {
        // The required-setup dialog passes ShowIntro="false" to avoid repeating itself. The pending-enrolment
        // warning must survive that, because it changes what the button will do.
        var api = new Mock<IMfaApiClient>();
        api.Setup(a => a.GetMfaStatusAsync()).ReturnsAsync(Result<MfaStatusDto>.Success(
            new MfaStatusDto(Enabled: false, PendingEnrollment: true, EnrolledAt: null, RecoveryCodesRemaining: 0)));

        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Head");

        var cut = RenderComponent<TwoFactorPanel>(p => p
            .Add(c => c.Flat, true)
            .Add(c => c.ShowIntro, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("A previous setup wasn't finished", cut.Markup);
            // The suppressed sentence really is suppressed.
            Assert.DoesNotContain("Add a second step to your sign-in", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public void TheEnrolledListIsReReadWhenTwoFactorIsSwitchedOn()
    {
        // The reported defect: the operator-recovery list inside this same dialog said "0 enrolled" moments after an
        // account had enrolled here, while the panel above it showed two-factor as on. The list initialises once, so it
        // has to be told to re-read when this panel changes what it lists — one dialog must not contradict itself.
        var api = new Mock<IMfaApiClient>();
        api.Setup(a => a.GetMfaStatusAsync()).ReturnsAsync(Result<MfaStatusDto>.Success(Off()));

        var enrolledCalls = 0;
        api.Setup(a => a.GetMfaEnrolledAccountsAsync())
           .ReturnsAsync(() =>
           {
               enrolledCalls++;
               return Result<IReadOnlyList<MfaEnrolledAccountDto>>.Success(Array.Empty<MfaEnrolledAccountDto>());
           });

        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Head");

        var cut = RenderComponent<TwoFactorPanel>(p => p
            .Add(c => c.Flat, true)
            .Add(c => c.ShowOperatorRecovery, true));

        cut.WaitForAssertion(() => Assert.True(enrolledCalls >= 1, "the recovery list never loaded"), RenderTimeout);
        var afterFirstLoad = enrolledCalls;

        // Re-reading the status is what happens after enrolling, disabling, or regenerating codes.
        cut.InvokeAsync(() => cut.Instance.RefreshStatusAsync());

        cut.WaitForAssertion(
            () => Assert.True(enrolledCalls > afterFirstLoad, "the recovery list was not re-read after the status changed"),
            RenderTimeout);
    }

    [Fact]
    public void TheOnNoticeIsNotColourCoded()
    {
        // A government screen states the position; it does not congratulate the officer in green. Asserted because the
        // green success styling was reintroduced once already.
        //
        // Checks EVERY component stylesheet, not just this panel's. Written against one file first, it passed while a
        // second copy of .mfa-notice-ok — in the recovery panel, which cannot share scoped CSS — was still green. The same
        // mistake as asserting a query filter merely exists: the assertion has to cover everywhere the rule is written.
        var componentCss = Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "EEMOCantilanSDS.Client", "Components"), "*.css", SearchOption.AllDirectories);

        var green = new List<string>();
        foreach (var file in componentCss)
        {
            var css = File.ReadAllText(file);
            var at = css.IndexOf(".mfa-notice-ok", StringComparison.Ordinal);
            if (at < 0) continue;

            var rule = css[at..];
            rule = rule[..rule.IndexOf('}')];
            if (rule.Contains("--green", StringComparison.Ordinal))
                green.Add(Path.GetFileName(file));
        }

        Assert.True(green.Count == 0, "These stylesheets still colour the success notice green: " + string.Join(", ", green));
    }

    /// <summary>Walks up from the test assembly to the repository root, so the CSS assertion works from any bin path.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
