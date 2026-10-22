using System;
using System.IO;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>
/// Regression guard for office-issued temporary passwords: the password-change endpoint
/// is authorized, so it must never be placed on the anonymous login HTTP client.
/// </summary>
public sealed class PasswordChangeAuthRoutingTests
{
    private static string Source(params string[] paths)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EEMOCantilanSDS.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(new[] { root!.FullName }.Concat(paths).ToArray()));
    }

    [Fact]
    public void PasswordChangeIsSentByAuthenticatedClient_AndProxyCanRefreshItsCookie()
    {
        var registration = Source("EEMOCantilanSDS.Client", "DependencyInjection.cs");
        var proxy = Source("EEMOCantilanSDS.Client", "Controllers", "AuthProxyController.cs");
        var apiClient = Source("EEMOCantilanSDS.HttpClients", "ApiClients", "AccountSecurityApiClient.cs");

        Assert.Contains("AddApiHttpClient<IAccountSecurityApiClient, AccountSecurityApiClient>", registration);
        Assert.Contains("accountSecurityApi.ChangeMyPasswordAsync(request)", proxy);
        Assert.Contains("await SignInWithTokensAsync(result.Value.AccessToken, result.Value.RefreshToken)", proxy);
        Assert.Contains("api/AdminAuth/change-my-password", apiClient);
    }

    [Fact]
    public void AnonymousSignInClientDoesNotExposeTheProtectedPasswordChange()
    {
        var anonymousClient = Source("EEMOCantilanSDS.HttpClients", "ApiClients", "AuthApiClient.cs");
        var anonymousContract = Source("EEMOCantilanSDS.Application", "Common", "Interface", "ApiClients", "IAuthApiClient.cs");

        Assert.DoesNotContain("ChangeMyPasswordAsync", anonymousClient);
        Assert.DoesNotContain("ChangeMyPasswordAsync", anonymousContract);
    }

    [Fact]
    public void AccountsHoverPreservesDarkTextContrast()
    {
        var styles = Source("EEMOCantilanSDS.Client", "Components", "Pages", "Menus", "Accounts.razor.css");
        Assert.Contains(".data-table.registry-table tbody tr:hover > td", styles);
        Assert.DoesNotContain("background: var(--accent-hover);", styles);
    }
}
