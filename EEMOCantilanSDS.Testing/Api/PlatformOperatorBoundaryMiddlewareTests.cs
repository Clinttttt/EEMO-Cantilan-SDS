using System.Security.Claims;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.AspNetCore.Http;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The platform operator may reach the platform's own endpoints and nothing belonging to a municipality.
/// </summary>
/// <remarks>
/// Closing the operator's SIGN-IN was necessary and not sufficient. The token minted by <c>console-login</c> is identical to a
/// municipal one - role SuperAdmin, MunicipalityId set to the default municipality - and municipal controllers authorise on role
/// alone, so a console session could still read that LGU's records by calling the API directly.
///
/// <para>Both directions are tested, because getting the allow-list wrong one way exposes an office's records and the other way locks
/// the operator out of the platform it runs.</para>
/// </remarks>
public class PlatformOperatorBoundaryMiddlewareTests
{
    private static async Task<bool> ReachedAsync(string path, bool authenticated, bool isOperator)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (isOperator) claims.Add(new Claim(AppClaimTypes.PlatformOperator, "true"));

        context.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            : new ClaimsPrincipal(new ClaimsIdentity(claims));

        var reached = false;
        var middleware = new PlatformOperatorBoundaryMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);

        return reached;
    }

    /// <summary>
    /// A municipality's own records are refused to the operator.
    /// </summary>
    /// <remarks>
    /// These are the endpoints the audit showed were reachable: authorised on <c>[Authorize(Roles = "SuperAdmin,Admin")]</c>, with the
    /// tenant resolved from the operator's own default-municipality claim.
    /// </remarks>
    [Theory]
    [InlineData("/api/Stalls")]
    [InlineData("/api/Facilities")]
    [InlineData("/api/Reports/month-end")]
    [InlineData("/api/Payments")]
    [InlineData("/api/Collectors")]
    [InlineData("/api/Mobile/npm/collections")]
    [InlineData("/api/TenantUsage/backups")]
    public async Task TheOperatorIsRefusedAMunicipalitysRecords(string path)
    {
        Assert.False(await ReachedAsync(path, authenticated: true, isOperator: true));
    }

    /// <summary>
    /// The platform's own work still reaches the operator, or the console it runs on stops working.
    /// </summary>
    /// <remarks>
    /// This list is what the admin console actually calls, read from its own sources rather than assumed: activation, assessment,
    /// onboarding, municipalities, platform-setup and adminauth.
    /// </remarks>
    [Theory]
    [InlineData("/api/adminauth/console-login")]
    [InlineData("/api/adminauth/refresh-token")]
    [InlineData("/api/adminauth/logout")]
    [InlineData("/api/adminauth/forgot-password")]
    [InlineData("/api/platform-setup/status")]
    [InlineData("/api/platform-setup/create-first-operator")]
    [InlineData("/api/assessment/requests")]
    [InlineData("/api/activation/municipality")]
    [InlineData("/api/onboarding/by-request/abc")]
    [InlineData("/api/municipalities")]
    [InlineData("/health")]
    public async Task ThePlatformsOwnWorkStillReachesTheOperator(string path)
    {
        Assert.True(await ReachedAsync(path, authenticated: true, isOperator: true));
    }

    /// <summary>
    /// An ordinary LGU account is untouched, which is the half that must not break.
    /// </summary>
    [Theory]
    [InlineData("/api/Stalls")]
    [InlineData("/api/Reports/month-end")]
    [InlineData("/api/TenantUsage/backups")]
    [InlineData("/api/adminauth/current-user")]
    public async Task AnOrdinaryOfficeAccountIsUnaffected(string path)
    {
        Assert.True(await ReachedAsync(path, authenticated: true, isOperator: false));
    }

    /// <summary>
    /// An anonymous request is not this middleware's business.
    /// </summary>
    /// <remarks>
    /// Sign-in, the payment webhook and the branding a login page reads are all anonymous. Judging them here would break them, and
    /// they are already answered by their own authorisation.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousRequestPassesThrough()
    {
        Assert.True(await ReachedAsync("/api/Stalls", authenticated: false, isOperator: false));
    }

    /// <summary>
    /// The refusal is a 403 carrying a code, so a client can act on it without reading prose.
    /// </summary>
    [Fact]
    public async Task TheRefusalIsForbiddenWithAMachineReadableCode()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/Stalls";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AppClaimTypes.PlatformOperator, "true")], "TestAuth"));

        await new PlatformOperatorBoundaryMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains(PlatformOperatorBoundaryMiddleware.Code, body);
    }
}
