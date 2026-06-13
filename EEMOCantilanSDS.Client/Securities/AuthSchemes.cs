namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// Cookie authentication scheme names. Admin and payor use SEPARATE cookies so they never clobber
/// each other in the same browser. A virtual "selector" policy scheme is the default: it forwards each
/// request to the right cookie based on whether the path is in the payor area.
/// </summary>
public static class AuthSchemes
{
    /// <summary>Virtual default scheme that routes to Admin/Payor by request path.</summary>
    public const string Selector = "EEMO";

    /// <summary>Admin/head web session (cookie ".EEMO.Admin").</summary>
    public const string Admin = "AdminCookie";

    /// <summary>Payor portal session (cookie ".EEMO.Payor").</summary>
    public const string Payor = "PayorCookie";
}
