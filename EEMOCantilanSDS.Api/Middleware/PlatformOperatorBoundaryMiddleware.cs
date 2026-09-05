using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Api.Middleware;

/// <summary>
/// The platform operator may reach the platform's own endpoints and nothing belonging to a municipality.
/// </summary>
/// <remarks>
/// <para>
/// The sign-in door was closed first: <c>LoginCommandHandler</c> refuses the operator a municipal portal session. That was necessary
/// and not sufficient, and an audit was right to say so. <c>TokenService</c> mints the SAME token for <c>console-login</c> as for the
/// municipal login - role <c>SuperAdmin</c> and <c>MunicipalityId</c> set to the DEFAULT municipality, because the operator account is
/// provisioned under it for tenant context. Municipal controllers authorise on role alone, and the tenant filter resolves from that
/// same claim. So a console session could still read that one LGU's stalls, collections and reports by calling the API directly. The
/// screen was shut; the boundary was not.
/// </para>
///
/// <para>
/// DEFAULT DENY, allow-listed by prefix, and that direction is deliberate. Listing the municipal endpoints to forbid would mean every
/// new controller was exposed until somebody remembered to add it - forty controllers today and more later. Listing what the operator
/// legitimately needs is a short list that fails closed: a route nobody has classified is refused, which is noisy and safe rather than
/// silent and open.
/// </para>
///
/// <para>
/// The list is exactly what the operator's own console calls, enumerated from the admin app's own sources rather than assumed:
/// activation, assessment, onboarding, municipalities, platform-setup and adminauth. Nothing else is on it, because nothing else was
/// found to call it. It is asserted by tests, because getting this list wrong in the other direction locks the operator out of the
/// platform it runs.
/// </para>
///
/// <para>
/// Enforced at the API, not the console, for the reason the sibling middleware gives: a client is a client, and a guard that lives
/// only in a browser is a suggestion.
/// </para>
/// </remarks>
public class PlatformOperatorBoundaryMiddleware(RequestDelegate next)
{
    /// <summary>Sent as the error code so a client can act on it without parsing prose.</summary>
    public const string Code = "operator_scope";

    /// <summary>
    /// What the operator's console legitimately calls. Prefix matches, lower-case, leading slash.
    /// </summary>
    /// <remarks>
    /// Derived from the admin console's own request paths. <c>adminauth</c> is here because an operator must be able to sign in,
    /// refresh, sign out and recover its own password; <c>municipalities</c> because the console lists the LGUs it onboards. The
    /// whole-database backup and restore endpoints are NOT here: they already carry the <c>PlatformOperator</c> policy, and they are
    /// driven by the GitHub workflows in practice, so nothing needs them opened through this door.
    /// </remarks>
    private static readonly string[] OperatorEndpoints =
    [
        "/api/activation",
        "/api/adminauth",
        "/api/assessment",
        "/api/municipalities",
        "/api/onboarding",
        "/api/platform-setup",
        "/health",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsOutsideTheOperatorsBusiness(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "The platform operator account cannot read or change a municipality's records.",
                code = Code,
            });
            return;
        }

        await next(context);
    }

    private static bool IsOutsideTheOperatorsBusiness(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true) return false;

        // Only the dedicated operator is affected. Every ordinary LGU account carries no such claim and is untouched by this
        // middleware, which is the half that must not break.
        var isOperator = context.User.FindFirst(AppClaimTypes.PlatformOperator)?.Value;
        if (!string.Equals(isOperator, "true", StringComparison.OrdinalIgnoreCase)) return false;

        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;

        return !OperatorEndpoints.Any(allowed =>
            path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));
    }
}
