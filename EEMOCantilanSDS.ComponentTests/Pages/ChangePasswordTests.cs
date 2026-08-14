using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Securities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using ChangePassword = EEMOCantilanSDS.Client.Components.Pages.ChangePassword;

/// <summary>
/// The change-password card.
///
/// <para>
/// Reported from use: the fields were narrow enough to truncate their own placeholders, the reveal control sat outside the box
/// it belonged to, and the confirm field had no reveal at all — so a rejected change gave no way to tell a typo from a wrong
/// password. The cause was CSS (see the stylesheet's own note), but two behaviours are worth holding here so they cannot
/// regress: every masked field has its own reveal, and a required change offers a way out.
/// </para>
/// </summary>
public class ChangePasswordTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    private IRenderedComponent<ChangePassword> RenderPage(bool required)
        => RenderPage(required, branding: null);

    private IRenderedComponent<ChangePassword> RenderPage(bool required, MunicipalityBrandingDto? branding)
    {
        this.AddTestAuthorization().SetAuthorized("cly.sullano");

        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(m => m.GetCurrentBrandingAsync()).ReturnsAsync(
            branding is null
                ? Result<MunicipalityBrandingDto>.Failure("no branding")
                : Result<MunicipalityBrandingDto>.Success(branding));

        // Injected into every component by the shared _Imports; stubbed so the page resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(municipalities.Object);                  // BrandingState's own dependency
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        // The page's own dependency. Built from real parts rather than mocked: nothing here calls it, and the tests that do
        // exercise the change itself live with the handler.
        Services.AddSingleton(sp => new AuthService(
            sp.GetRequiredService<IJSRuntime>(),
            sp.GetRequiredService<NavigationManager>(),
            new AuthStateProvider(Mock.Of<IHttpContextAccessor>()),
            new TokenService(),
            NullLogger<AuthService>.Instance));

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(required ? "/change-password?required=1" : "/change-password");

        return RenderComponent<ChangePassword>();
    }

    private static MunicipalityBrandingDto Branding(string code, string name, string officeAcronym, string officeName) =>
        new(Code: code, TenantCode: code.ToLowerInvariant(), Name: name, Province: "Surigao del Sur",
            OfficeName: officeName, SealPath: null, Status: "Active", IsActive: true, OfficeAcronym: officeAcronym);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryMaskedFieldHasItsOwnReveal(bool required)
    {
        var page = RenderPage(required);

        // Three masked fields — current, new, confirm — so three reveal controls. The confirm field previously had none.
        page.WaitForAssertion(
            () => Assert.Equal(3, page.FindAll("input[type=password]").Count), RenderTimeout);
        Assert.Equal(3, page.FindAll("button.form-eye-btn").Count);
    }

    [Fact]
    public void RevealingOneFieldDoesNotRevealTheOthers()
    {
        var page = RenderPage(required: true);
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("button.form-eye-btn").Count), RenderTimeout);

        page.FindAll("button.form-eye-btn")[1].Click();   // the new-password field

        page.WaitForAssertion(() =>
        {
            Assert.Equal("text", page.Find("#cp-new").GetAttribute("type"));
            Assert.Equal("password", page.Find("#cp-current").GetAttribute("type"));
            Assert.Equal("password", page.Find("#cp-confirm").GetAttribute("type"));
        }, RenderTimeout);
    }

    [Fact]
    public void ThePanelShowsTheSignedInLgu_NotTheDefaultOne()
    {
        // Multi-tenancy, on the one screen where it is easiest to get wrong: the branding endpoint is blocked for a session
        // that must change its password unless it is allow-listed, and BrandingState falls back to Cantilan when unresolved.
        // An officer of another municipality must never be shown Cantilan's seal, office or name.
        var page = RenderPage(required: true, Branding("MADRID", "Madrid", "MEEO", "Municipal Economic Enterprise Office"));

        page.WaitForAssertion(() =>
        {
            var markup = page.Markup;
            Assert.Contains("Municipality of Madrid", markup);
            Assert.Contains("MEEO", markup);
            Assert.DoesNotContain("Cantilan", markup);
            Assert.DoesNotContain("LGU_CANTILAN_LOGO", markup);
        }, RenderTimeout);
    }

    [Fact]
    public void WhenBrandingCannotBeResolvedTheLeftPanelClaimsNobodysIdentity()
    {
        // A failed load must not fall back to the default LGU's seal and office name here. Neutral is the honest answer: the
        // form is still usable, and no municipality is misrepresented.
        var page = RenderPage(required: true, branding: null);

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("LGU_CANTILAN_LOGO", page.Markup);
            Assert.DoesNotContain("Municipality of Cantilan", page.Markup);
            Assert.NotNull(page.Find("button.setup-submit"));
        }, RenderTimeout);
    }

    [Fact]
    public void ARequiredChangeOffersSigningOut_NotCancel()
    {
        // With no app shell there is no menu to sign out from. A required change that also had no way out would leave the
        // account with nowhere to go but the browser's back button, which the API refuses anyway.
        var page = RenderPage(required: true);

        page.WaitForAssertion(() =>
        {
            var secondary = page.Find("button.cp-secondary");
            Assert.Contains("Sign out", secondary.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void AnOptionalChangeOffersCancel()
    {
        var page = RenderPage(required: false);

        page.WaitForAssertion(() =>
        {
            var secondary = page.Find("button.cp-secondary");
            Assert.Contains("Cancel", secondary.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void TheRuleIsStatedBeforeItIsEnforced()
    {
        // The requirement is shown next to the field rather than only reported after a rejection, and it must match what the
        // server actually enforces: 8 characters, a letter and a digit.
        var page = RenderPage(required: true);

        page.WaitForAssertion(() =>
        {
            var hint = page.Find("#cp-rule").TextContent;
            Assert.Contains("8", hint);
            Assert.Contains("letter", hint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("number", hint, StringComparison.OrdinalIgnoreCase);
        }, RenderTimeout);
    }

    [Theory]
    [InlineData("Abcdefg1", "Abcdefg2", "do not match")]        // mismatched repeat
    [InlineData("short1", "short1", "at least 8")]              // too short
    [InlineData("abcdefghij", "abcdefghij", "number")]          // no digit
    [InlineData("12345678", "12345678", "letter")]              // no letter
    public void TheFormSaysWhatIsWrongWithoutAskingTheServer(string newPassword, string confirm, string expected)
    {
        var page = RenderPage(required: true);
        page.WaitForAssertion(() => page.Find("#cp-current"), RenderTimeout);

        page.Find("#cp-current").Input("office-issued");
        page.Find("#cp-new").Input(newPassword);
        page.Find("#cp-confirm").Input(confirm);
        page.Find("button.setup-submit").Click();

        page.WaitForAssertion(
            () => Assert.Contains(expected, page.Find(".form-error").TextContent, StringComparison.OrdinalIgnoreCase),
            RenderTimeout);
    }
}
