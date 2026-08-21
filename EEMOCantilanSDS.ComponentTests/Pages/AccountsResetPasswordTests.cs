using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Accounts = EEMOCantilanSDS.Client.Components.Pages.Menus.Accounts;

/// <summary>
/// The Reset Password dialog on the Staff Accounts page.
///
/// <para>
/// Both behaviours here were reported from use. The dialog addressed the signed-in officer in the third person — telling
/// them to "share it securely" with themselves — and its confirm field was masked with no way to reveal it, so a refused
/// reset gave no way to tell a typo from a wrong password.
/// </para>
/// </summary>
public class AccountsResetPasswordTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    private static readonly Guid SignedInHeadId = Guid.NewGuid();
    private static readonly Guid OtherAdminId = Guid.NewGuid();

    private static AdminListDto Admin(Guid id, string name, AdminRole role) => new(
        id, name, name.ToLowerInvariant().Replace(" ", ""), $"{id:N}@example.gov.ph",
        role, IsActive: true, MustChangePassword: false, LastLoginAt: null, CreatedAt: DateTime.UtcNow);

    private IRenderedComponent<Accounts> RenderAccounts()
    {
        var admins = new Mock<IAdminsApiClient>();
        admins.Setup(a => a.GetAllAdminsAsync()).ReturnsAsync(
            Result<IReadOnlyList<AdminListDto>>.Success(new[]
            {
                Admin(SignedInHeadId, "Cly Sullano", AdminRole.SuperAdmin),
                Admin(OtherAdminId, "Ana Reyes", AdminRole.Admin),
            }));

        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(c => c.GetAllCollectorsAsync())
                  .ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(Array.Empty<CollectorListDto>()));

        Services.AddSingleton(admins.Object);
        Services.AddSingleton(collectors.Object);
        // The shared _Imports injects these into every component; stub them so the page resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IMfaApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        // The page reads the signed-in user's id from the token to tell "my account" from someone else's.
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Cly Sullano");
        auth.SetRoles("SuperAdmin");
        auth.SetClaims(new System.Security.Claims.Claim("sub", SignedInHeadId.ToString()));

        return RenderComponent<Accounts>();
    }

    /// <summary>Opens the reset dialog for a row, found by the account's name rather than by position.</summary>
    private static void OpenResetFor(IRenderedComponent<Accounts> cut, string name)
    {
        var row = cut.FindAll("tr").First(r => r.TextContent.Contains(name, StringComparison.Ordinal));
        row.QuerySelectorAll("button.action-btn")
           .First(b => (b.GetAttribute("title") ?? string.Empty).Contains("Reset", StringComparison.OrdinalIgnoreCase))
           .Click();
    }

    [Fact]
    public void TheConfirmFieldCanBeRevealedAndHiddenAgain()
    {
        var cut = RenderAccounts();
        cut.WaitForAssertion(() => Assert.Contains("Cly Sullano", cut.Markup), RenderTimeout);

        OpenResetFor(cut, "Cly Sullano");

        var confirm = cut.WaitForElement("#reset-confirm", RenderTimeout);
        Assert.Equal("password", confirm.GetAttribute("type"));

        // Selected by class, never by position among the dialog's buttons.
        cut.Find("button.form-eye-btn").Click();
        Assert.Equal("text", cut.Find("#reset-confirm").GetAttribute("type"));

        cut.Find("button.form-eye-btn").Click();
        Assert.Equal("password", cut.Find("#reset-confirm").GetAttribute("type"));
    }

    [Fact]
    public void ResettingYourOwnAccountDoesNotTellYouToShareItWithYourself()
    {
        var cut = RenderAccounts();
        cut.WaitForAssertion(() => Assert.Contains("Cly Sullano", cut.Markup), RenderTimeout);

        OpenResetFor(cut, "Cly Sullano");
        cut.WaitForElement("#reset-confirm", RenderTimeout);

        Assert.Contains("your own account", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Give this password to the account holder", cut.Markup);
    }

    [Fact]
    public void ResettingSomeoneElsesAccountStillSaysToHandItOver()
    {
        // The other half of the same rule: the wording must still be about a hand-over when it IS someone else, or the
        // fix would simply have moved the misinformation.
        var cut = RenderAccounts();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Markup), RenderTimeout);

        OpenResetFor(cut, "Ana Reyes");
        cut.WaitForElement("#reset-confirm", RenderTimeout);

        Assert.Contains("Ana Reyes", cut.Markup);
        Assert.Contains("Give this password to the account holder", cut.Markup);
        Assert.DoesNotContain("your own account", cut.Markup);
    }

    [Fact]
    public void TheBackdropIsInert_SoAStrayClickCannotDiscardTheTypedPasswords()
    {
        // Reported from use. Every other dialog on this page closes when the backdrop is clicked, and for a confirm
        // prompt that is a convenience. This one holds a new password AND the officer's own password confirming it, so
        // the same gesture threw both away with no warning, mid-task, with nothing to recover them from.
        //
        // Asserted as the ABSENCE of a handler rather than by clicking and checking it survived: bUnit refuses to
        // dispatch a click to an element that handles none, so the refusal IS the assertion, and it cannot pass by the
        // dialog happening to reopen.
        var cut = RenderAccounts();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Markup), RenderTimeout);

        OpenResetFor(cut, "Ana Reyes");
        cut.WaitForElement("#reset-confirm", RenderTimeout);
        cut.Find("#reset-confirm").Input("my-own-password");

        Assert.Throws<Bunit.MissingEventHandlerException>(() => cut.Find(".eemo-modal-overlay").Click());

        // Still open, and still holding what was typed.
        Assert.Equal("my-own-password", cut.Find("#reset-confirm").GetAttribute("value"));
    }

    [Fact]
    public void TheCrossStillCloses_SoLeavingIsStillPossible()
    {
        // The other half: refusing the backdrop must not leave the officer stuck in the dialog.
        var cut = RenderAccounts();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Markup), RenderTimeout);

        OpenResetFor(cut, "Ana Reyes");
        cut.WaitForElement("#reset-confirm", RenderTimeout);

        cut.Find("button.eemo-modal-close").Click();

        Assert.Empty(cut.FindAll("#reset-confirm"));
    }
}
