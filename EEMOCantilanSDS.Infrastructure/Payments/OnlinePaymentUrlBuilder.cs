using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <summary>
/// Builds payor-portal return URLs from configured base URL (<c>OnlinePayments:PortalBaseUrl</c>),
/// so the gateway redirect targets are controlled server-side, not by the client.
/// </summary>
public sealed class OnlinePaymentUrlBuilder(
    IConfiguration configuration,
    Microsoft.AspNetCore.Http.IHttpContextAccessor? httpContextAccessor = null) : IOnlinePaymentUrlBuilder
{
    /// <summary>
    /// The public payor-portal origin. Resolved from <c>OnlinePayments:PortalBaseUrl</c>. This MUST be
    /// the Blazor portal that serves <c>/payor/payment/success</c> (e.g. <c>https://eemo.stalltrack.site</c>
    /// in production, <c>https://localhost:7167</c> locally) — NOT the API. It is also fail-closed: a
    /// missing value, or a localhost/loopback value outside Development, throws. Otherwise a deployment
    /// that forgot to override the dev default would redirect payors to localhost after checkout, breaking
    /// both the return screen and the on-return reconciliation.
    /// </summary>
    private string PortalBaseUrl
    {
        get
        {
            var configured = configuration["OnlinePayments:PortalBaseUrl"];
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException(
                    "OnlinePayments:PortalBaseUrl is not configured. Set it to the public payor portal URL " +
                    "(e.g. https://eemo.stalltrack.site).");

            var baseUrl = configured.TrimEnd('/');

            // Default to Production when the environment is unknown, so the guard is fail-closed.
            var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";
            var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);

            if (!isDevelopment
                && (baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                    || baseUrl.Contains("127.0.0.1", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"OnlinePayments:PortalBaseUrl is '{baseUrl}' in environment '{environment}'. A localhost " +
                    "portal URL would strand payors after checkout. Set it to the public payor portal URL " +
                    "(e.g. https://eemo.stalltrack.site).");
            }

            return baseUrl;
        }
    }

    public string BuildSuccessUrl(string reference) =>
        $"{PayorReturnBaseUrl}/payor/payment/success?ref={Uri.EscapeDataString(reference)}";

    public string BuildCancelUrl(string reference) =>
        $"{PayorReturnBaseUrl}/payor/payment/cancelled?ref={Uri.EscapeDataString(reference)}";

    /// <summary>
    /// Where THIS payor should land after checkout.
    ///
    /// <para>
    /// Two payor portals exist during the move to the Angular one, and each must return to itself: a payor who started
    /// on payor.stalltrack.site cannot be dropped on the other portal's screen, where their session does not exist.
    /// The browser tells us which one they are on, since a call from a browser app to this API is cross-origin and
    /// carries an <c>Origin</c> header. The Blazor portal calls this API server-to-server and so sends none, which is
    /// why it keeps returning to <c>OnlinePayments:PortalBaseUrl</c> with nothing to configure.
    /// </para>
    ///
    /// <para>
    /// The origin is never trusted as given. It is matched against <c>OnlinePayments:AllowedReturnOrigins</c>, and
    /// anything not on that list falls back to the configured portal. That keeps the gateway's redirect target decided
    /// by this server, which was the point of building these URLs here rather than accepting them from a client: a
    /// caller who could name the return address could send a payor to a page of their own after paying.
    /// </para>
    /// </summary>
    private string PayorReturnBaseUrl
    {
        get
        {
            var origin = httpContextAccessor?.HttpContext?.Request.Headers["Origin"].ToString();
            if (string.IsNullOrWhiteSpace(origin)) return PortalBaseUrl;

            var candidate = origin.Trim().TrimEnd('/');

            var allowed = configuration.GetSection("OnlinePayments:AllowedReturnOrigins").Get<string[]>()
                          ?? Array.Empty<string>();

            var permitted = allowed.Any(a =>
                !string.IsNullOrWhiteSpace(a)
                && string.Equals(a.Trim().TrimEnd('/'), candidate, StringComparison.OrdinalIgnoreCase));

            return permitted ? candidate : PortalBaseUrl;
        }
    }

    public string BuildWebhookUrl(string tenantCode)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
            throw new InvalidOperationException(
                "A webhook URL cannot be built without a tenant code. The tenant-less endpoint verifies against the " +
                "platform configuration, which is the default municipality's signing secret, so it must never be handed " +
                "to another LGU.");

        return $"{WebhookBaseUrl}/api/onlinepayments/webhook/{Uri.EscapeDataString(tenantCode.Trim())}";
    }

    /// <summary>
    /// The public origin of THIS API, which is what PayMongo has to be able to reach.
    ///
    /// <para>
    /// <c>OnlinePayments:WebhookBaseUrl</c> when set, so a deployment can pin it; otherwise the current request's own
    /// origin, which is this API answering on its public host. Deliberately not the payor portal's base URL - that serves
    /// the return screens, and pointing PayMongo at it would send every notification somewhere that cannot verify one.
    /// </para>
    ///
    /// <para>
    /// Fail-closed in the same way as the portal URL: a localhost origin outside Development throws, because a webhook
    /// registered against localhost is registered against nothing, and PayMongo would report it as failing forever.
    /// </para>
    /// </summary>
    private string WebhookBaseUrl
    {
        get
        {
            var configured = configuration["OnlinePayments:WebhookBaseUrl"];

            var baseUrl = !string.IsNullOrWhiteSpace(configured)
                ? configured!.TrimEnd('/')
                : RequestOrigin();

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException(
                    "Could not determine this API's public address for a PayMongo webhook. Set " +
                    "OnlinePayments:WebhookBaseUrl (for example https://api.stalltrack.site).");

            var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";
            var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);

            if (!isDevelopment
                && (baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                    || baseUrl.Contains("127.0.0.1", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"The webhook base URL is '{baseUrl}' in environment '{environment}'. PayMongo cannot reach a " +
                    "localhost address, so the LGU's payments would never confirm themselves. Set " +
                    "OnlinePayments:WebhookBaseUrl to this API's public address.");
            }

            return baseUrl;
        }
    }

    private string RequestOrigin()
    {
        var request = httpContextAccessor?.HttpContext?.Request;
        if (request is null) return string.Empty;

        var host = request.Host.ToString();

        // HTTPS unless this is a local loopback. PayMongo will not call an http address, and the scheme seen here is not
        // necessarily the public one: the portal reaches this API server-to-server, so a plain-http internal hop would
        // otherwise be handed to the office as the address to register - which the office saw.
        var isLocal = host.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                   || host.Contains("127.0.0.1", StringComparison.Ordinal);

        var scheme = isLocal ? request.Scheme : "https";
        return $"{scheme}://{host}";
    }
}
