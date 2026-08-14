using EEMOCantilanSDS.Application.Common;

namespace EEMOCantilanSDS.Api.Middleware;

/// <summary>
/// While an account is required to change its password, it may do nothing else.
///
/// <para>
/// The flag has existed since the beginning: the office issues a password, the account is marked, and the roster shows
/// "Reset pending". Nothing enforced it, so the promise the portal made — that the holder would be asked to change it — was
/// simply untrue, and an office-issued password could stay in use indefinitely.
/// </para>
///
/// <para>
/// Enforced at the API rather than only in the portal, because the portal is a client like any other: a guard that lives
/// only in the browser is a suggestion. The response is 403 with a machine-readable code so the portal can route to the
/// change-password screen rather than guess from a message.
/// </para>
///
/// <para>
/// The allow-list is deliberately short and explicit. Everything a blocked session legitimately needs is here — changing the
/// password, reading who it is, refreshing, signing out, and the health probes — and nothing else. Being wrong in the other
/// direction would lock the office out of its own system, so the list is asserted by tests rather than trusted.
/// </para>
/// </summary>
public class MustChangePasswordMiddleware(RequestDelegate next)
{
    /// <summary>Sent as the error code so the client can act on it without parsing prose.</summary>
    public const string Code = "password_change_required";

    private static readonly string[] Allowed =
    [
        "/api/adminauth/change-my-password",  // the only way out
        "/api/adminauth/current-user",        // the portal renders the shell from this
        "/api/adminauth/refresh-token",       // an expiring session must still be able to refresh
        "/api/adminauth/logout",              // and must always be able to leave
        // The change-password screen shows the signed-in LGU's own seal and office name. Without this it fell back to the
        // default municipality's branding, which would have shown one LGU's identity to another's officer. It exposes nothing
        // new: the same branding is already served ANONYMOUSLY by identifier for the sign-in page
        // (GET /api/municipalities/{identifier}/branding) — this route only scopes it to the caller's own token instead.
        "/api/municipalities/current/branding",
        "/health",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsBlocked(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Your password was set by the office. Please choose a new password before continuing.",
                code = Code,
            });
            return;
        }

        await next(context);
    }

    private static bool IsBlocked(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true) return false;

        var mustChange = context.User.FindFirst(AppClaimTypes.MustChangePassword)?.Value;
        if (!bool.TryParse(mustChange, out var required) || !required) return false;

        var path = context.Request.Path.Value ?? string.Empty;
        return !Allowed.Any(a => path.StartsWith(a, StringComparison.OrdinalIgnoreCase));
    }
}
