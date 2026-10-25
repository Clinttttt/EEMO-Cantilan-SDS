using System.Reflection;
using EEMOCantilanSDS.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EEMOCantilanSDS.Testing.Api;

/// <summary>
/// The API has no fallback authorization policy, so a controller action with neither [Authorize] nor [AllowAnonymous] is
/// ANONYMOUS - and an anonymous caller resolves to the default (Cantilan) tenant. VendorsController shipped that way and served
/// Cantilan's vendor registry to the internet (ST-11). Every action must now state its audience, and the anonymous set is pinned.
/// </summary>
public sealed class EveryEndpointStatesItsAuthorizationTests
{
    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .Select(m => (t, m)));

    [Fact]
    public void NoActionIsLeftWithoutAnAuthorizationStatement()
    {
        var unstated = Actions()
            .Where(a => !a.Controller.GetCustomAttributes<AuthorizeAttribute>(true).Any()
                     && !a.Controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()
                     && !a.Action.GetCustomAttributes<AuthorizeAttribute>(true).Any()
                     && !a.Action.GetCustomAttributes<AllowAnonymousAttribute>(true).Any())
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .ToList();

        Assert.True(unstated.Count == 0, "Anonymous by omission: " + string.Join(", ", unstated));
    }

    [Fact]
    public void TheAnonymousSurfaceIsExactlyTheDocumentedOne()
    {
        var anonymous = Actions()
            .Where(a => a.Action.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()
                     || (a.Controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()
                         && !a.Action.GetCustomAttributes<AuthorizeAttribute>(true).Any()))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(string.Join("\n", Expected), string.Join("\n", anonymous));
    }

    // Pre-authentication by nature: sign-in, token refresh, single-use-secret flows, the signature-verified payment webhook, public branding.
    private static readonly string[] Expected =
    [
        "ActivationController.GetContextAsync",
        "ActivationController.SetPasswordAsync",
        "AdminAuthController.ConsoleLoginAsync",
        "AdminAuthController.ForgotPasswordAsync",
        "AdminAuthController.GetResetContextAsync",
        "AdminAuthController.LoginAsync",
        "AdminAuthController.Logout",
        "AdminAuthController.RefreshAsync",
        "AdminAuthController.ResetPasswordAsync",
        "AdminAuthController.VerifyEmailAsync",
        "AdminAuthController.VerifyMfaLoginAsync",
        "AssessmentController.Submit",
        "CollectorAuthController.LoginAsync",
        "CollectorAuthController.RefreshAsync",
        "MobileBindController.ResolveAsync",
        "MobileVersionController.GetVersion",
        "MunicipalitiesController.GetBranding",
        "MunicipalitiesController.GetMunicipalities",
        "MunicipalitiesController.GetSeal",
        "OnboardingController.GetDraft",
        "OnboardingController.Submit",
        "OnboardingController.UpdateConfig",
        "OnlinePaymentsController.WebhookAsync",
        "OnlinePaymentsController.WebhookForTenantAsync",
        "PayorAuthController.ActivateAsync",
        "PayorAuthController.LoginAsync",
        "PayorAuthController.RefreshAsync",
        "PlatformSetupController.CreateFirstOperator",
        "PlatformSetupController.GetStatus",
        "SetupController.CreateFirstAdmin",
        "SetupController.GetSetupStatus",
    ];
}
