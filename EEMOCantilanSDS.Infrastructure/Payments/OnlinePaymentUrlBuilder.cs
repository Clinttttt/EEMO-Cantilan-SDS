using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <summary>
/// Builds payor-portal return URLs from configured base URL (<c>OnlinePayments:PortalBaseUrl</c>),
/// so the gateway redirect targets are controlled server-side, not by the client.
/// </summary>
public sealed class OnlinePaymentUrlBuilder(IConfiguration configuration) : IOnlinePaymentUrlBuilder
{
    private string PortalBaseUrl =>
        (configuration["OnlinePayments:PortalBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');

    public string BuildSuccessUrl(string reference) =>
        $"{PortalBaseUrl}/payor/payment/success?ref={Uri.EscapeDataString(reference)}";

    public string BuildCancelUrl(string reference) =>
        $"{PortalBaseUrl}/payor/payment/cancelled?ref={Uri.EscapeDataString(reference)}";
}
