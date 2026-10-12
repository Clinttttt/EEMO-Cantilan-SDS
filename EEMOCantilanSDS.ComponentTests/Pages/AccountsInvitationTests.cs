using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Accounts = EEMOCantilanSDS.Client.Components.Pages.Menus.Accounts;

/// <summary>
/// The Head invites an Admin instead of issuing a password: the invitation is the default, no password field is shown for it, the
/// one-time link is displayed once, and a pending invitation reads as pending with its own resend / withdraw controls.
/// </summary>
public class AccountsInvitationTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Guid HeadId = Guid.NewGuid();

    private static AdminListDto Row(Guid id, string name, AdminRole role, bool active, bool pending = false) => new(
        id, name, name.ToLowerInvariant().Replace(" ", ""), $"{id:N}@example.gov.ph", role, active, false, null, DateTime.UtcNow,
        EmailVerified: active, InvitationPending: pending);

    private (IRenderedComponent<Accounts> Cut, Mock<IAdminsApiClient> Api) Render(params AdminListDto[] extra)
    {
        var api = new Mock<IAdminsApiClient>();
        api.Setup(a => a.GetAllAdminsAsync()).ReturnsAsync(Result<IReadOnlyList<AdminListDto>>.Success(
            new[] { Row(HeadId, "Cly Sullano", AdminRole.SuperAdmin, true) }.Concat(extra).ToArray()));
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(c => c.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(Array.Empty<CollectorListDto>()));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(collectors.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IMfaApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Cly Sullano");
        auth.SetRoles("SuperAdmin");
        auth.SetClaims(new System.Security.Claims.Claim("sub", HeadId.ToString()));
        return (RenderComponent<Accounts>(), api);
    }

    [Fact]
    public void CreatingAnAdmin_DefaultsToAnInvitation_WithNoPasswordFieldAndNoPasswordIssued()
    {
        var (cut, api) = Render();
        cut.WaitForAssertion(() => Assert.Contains("Cly Sullano", cut.Markup), Timeout);
        api.Setup(a => a.InviteAdminAsync(It.IsAny<InviteAdminCommand>())).ReturnsAsync(Result<AdminInvitationDto>.Success(
            new AdminInvitationDto(new AdminDto(Guid.NewGuid(), "Maria Santos", "maria.santos", "maria@lgu.gov.ph", AdminRole.Admin, false),
                "https://portal.example/activate/ONE-TIME-LINK", EmailSent: true, DateTime.UtcNow.AddDays(7))));

        cut.Find("button.btn-primary:not([disabled])").Click(); // the page's "new account" button
        var drawerOpen = cut.FindAll("button").First(b => b.TextContent.Contains("Send Invitation"));
        Assert.DoesNotContain("Temporary Password", cut.Markup);   // nothing to type or share

        cut.Find("input[placeholder='e.g. Maria Santos']").Input("Maria Santos");
        cut.Find("input[placeholder='Login username']").Input("maria.santos");
        cut.Find("input[type=email]").Input("maria@lgu.gov.ph");
        drawerOpen.Click();

        api.Verify(a => a.InviteAdminAsync(It.Is<InviteAdminCommand>(c =>
            c.FullName == "Maria Santos" && c.Username == "maria.santos" && c.Email == "maria@lgu.gov.ph" && c.Role == AdminRole.Admin)), Times.Once);
        api.Verify(a => a.CreateAdminAsync(It.IsAny<EEMOCantilanSDS.Application.Command.Admins.CreateAdmin.CreateAdminCommand>()), Times.Never);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("https://portal.example/activate/ONE-TIME-LINK", cut.Find("input[aria-label='Invitation link']").GetAttribute("value"));
            Assert.Contains("shown only now", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void TheTemporaryPasswordRoute_StillExists_AsAnExplicitChoice()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() => Assert.Contains("Cly Sullano", cut.Markup), Timeout);

        cut.Find("button.btn-primary:not([disabled])").Click();
        cut.FindAll("button.role-opt").First(b => b.TextContent.Contains("Set a temporary password")).Click();

        Assert.Contains("Temporary Password", cut.Markup);
        Assert.Contains("Create Account", cut.Markup);
        Assert.Contains("must change it at their first sign-in", cut.Markup);
    }

    [Fact]
    public void APendingInvitation_ReadsAsPending_WithResendAndWithdraw_AndNotAsAnOrdinaryInactiveAccount()
    {
        var (cut, api) = Render(Row(Guid.NewGuid(), "Pending Person", AdminRole.Admin, active: false, pending: true));
        cut.WaitForAssertion(() => Assert.Contains("Pending Person", cut.Markup), Timeout);

        var row = cut.FindAll("tr").First(r => r.TextContent.Contains("Pending Person"));
        Assert.Contains("Invitation pending", row.TextContent);
        var titles = row.QuerySelectorAll("button.action-btn").Select(b => b.GetAttribute("title") ?? "").ToArray();
        Assert.Contains(titles, t => t.StartsWith("Send a new invitation link", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.StartsWith("Withdraw this invitation", StringComparison.Ordinal));
        // The ordinary controls for an account in use do not pretend this is one.
        Assert.DoesNotContain(titles, t => t.Contains("confirmation link", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(api); // shape only: the row is rendered from the roster the API returned
    }
}
