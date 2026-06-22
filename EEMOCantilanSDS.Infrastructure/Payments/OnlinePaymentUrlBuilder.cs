using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <summary>
/// Builds payor-portal return URLs from configured base URL (<c>OnlinePayments:PortalBaseUrl</c>),
/// so the gateway redirect targets are controlled server-side, not by the client.
/// </summary>
public sealed class OnlinePaymentUrlBuilder(IConfiguration configuration) : IOnlinePaymentUrlBuilder
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
        $"{PortalBaseUrl}/payor/payment/success?ref={Uri.EscapeDataString(reference)}";

    public string BuildCancelUrl(string reference) =>
        $"{PortalBaseUrl}/payor/payment/cancelled?ref={Uri.EscapeDataString(reference)}";
}
