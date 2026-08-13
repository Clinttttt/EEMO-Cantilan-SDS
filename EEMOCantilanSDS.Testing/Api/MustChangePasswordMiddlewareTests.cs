using System.Security.Claims;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application.Common;
using Microsoft.AspNetCore.Http;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The gate that makes a required password change real.
///
/// <para>
/// The flag existed from the beginning and nothing enforced it, so the portal's promise — that an office-issued password
/// would have to be replaced — was untrue, and such a password could stay in use indefinitely.
/// </para>
///
/// <para>
/// These tests exist mostly to police the ALLOW-LIST. Blocking too little makes the requirement cosmetic; blocking too much
/// locks the office out of its own system, including out of the very screen that would fix it. Both directions are asserted.
/// </para>
/// </summary>
public class MustChangePasswordMiddlewareTests
{
    private static async Task<HttpContext> RunAsync(string path, bool authenticated, bool? mustChange)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (authenticated)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            if (mustChange is { } flag)
                claims.Add(new Claim(AppClaimTypes.MustChangePassword, flag.ToString()));

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        var reached = false;
        var middleware = new MustChangePasswordMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);

        context.Items["reached"] = reached;
        return context;
    }

    private static bool Reached(HttpContext c) => (bool)c.Items["reached"]!;

    [Theory]
    [InlineData("/api/Facilities")]
    [InlineData("/api/Stalls/123")]
    [InlineData("/api/Payments/record")]
    [InlineData("/api/Reports/financial")]
    [InlineData("/api/Admins")]
    [InlineData("/api/Backup/create")]
    public async Task AnAccountThatMustChangeItsPasswordCannotDoAnythingElse(string path)
    {
        var context = await RunAsync(path, authenticated: true, mustChange: true);

        Assert.False(Reached(context), $"{path} was still handled");
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task TheRefusalCarriesACodeTheClientCanActOn()
    {
        // The portal routes on the code, not the prose: a message can be reworded, and a client that pattern-matches English
        // breaks silently when it is.
        var context = await RunAsync("/api/Facilities", authenticated: true, mustChange: true);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Contains(MustChangePasswordMiddleware.Code, body);
    }

    [Theory]
    [InlineData("/api/AdminAuth/change-my-password")]   // the only way out
    [InlineData("/api/AdminAuth/current-user")]         // the portal shell reads this
    [InlineData("/api/AdminAuth/refresh-token")]        // an expiring session must still refresh
    [InlineData("/api/AdminAuth/logout")]               // and must always be able to leave
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task TheWayOutIsNeverBlocked(string path)
    {
        var context = await RunAsync(path, authenticated: true, mustChange: true);

        Assert.True(Reached(context), $"{path} was blocked, which would lock the office out");
    }

    [Fact]
    public async Task TheAllowListIsMatchedCaseInsensitively()
    {
        // The route is api/[controller], so the framework's own casing is "AdminAuth" while links and clients often use
        // lower case. A case-sensitive list would block the escape route depending on how it was typed.
        var lower = await RunAsync("/api/adminauth/change-my-password", authenticated: true, mustChange: true);
        var upper = await RunAsync("/api/ADMINAUTH/CHANGE-MY-PASSWORD", authenticated: true, mustChange: true);

        Assert.True(Reached(lower));
        Assert.True(Reached(upper));
    }

    [Fact]
    public async Task AnAccountWithNothingToChangeIsUnaffected()
    {
        var withFlagFalse = await RunAsync("/api/Facilities", authenticated: true, mustChange: false);
        var withNoFlagAtAll = await RunAsync("/api/Facilities", authenticated: true, mustChange: null);

        Assert.True(Reached(withFlagFalse));
        // Collectors and payors carry no such claim, and neither do older tokens issued before it existed. Absent must mean
        // "not required" — treating a missing claim as a requirement would lock out every account that has one.
        Assert.True(Reached(withNoFlagAtAll));
    }

    [Fact]
    public async Task AnAnonymousRequestIsLeftToTheOrdinaryAuthenticationRules()
    {
        // Sign-in itself must not be answered with "change your password first".
        var context = await RunAsync("/api/AdminAuth/login", authenticated: false, mustChange: null);

        Assert.True(Reached(context));
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public void TheGateIsActuallyWiredIntoThePipeline()
    {
        // Everything above tests the middleware by calling it directly, which says nothing about whether the application
        // USES it — deleting the one registration line leaves all of those tests green while the requirement quietly stops
        // being enforced. Verified by doing exactly that, and finding the suite still passed.
        //
        // A source assertion is brittle by nature, and earns it here: it is the only thing standing between a one-line
        // deletion and a security control that exists but never runs. Its ORDER matters too — after authorization, so the
        // claim exists and an anonymous caller still gets the ordinary 401.
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "EEMOCantilanSDS.Api", "Program.cs"));

        var registration = program.IndexOf($"UseMiddleware<{nameof(MustChangePasswordMiddleware)}>", StringComparison.Ordinal);
        var authorization = program.IndexOf("UseAuthorization()", StringComparison.Ordinal);
        var mapControllers = program.IndexOf("MapControllers()", StringComparison.Ordinal);

        Assert.True(registration > 0, "the API does not register MustChangePasswordMiddleware, so nothing enforces the requirement");
        Assert.True(registration > authorization, "the gate must run after authorization, or the claim is not yet available");
        Assert.True(registration < mapControllers, "the gate must run before the endpoints, or a blocked request reaches a handler");
    }

    /// <summary>Walks up from the test assembly to the repository root, so this works from any bin path.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
