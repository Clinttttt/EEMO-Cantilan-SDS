using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <summary>
/// PayMongo's expire operation is the authority: an active checkout is not assumed abandoned
/// merely because its QR code timed out. Always re-read the provider before changing local state.
/// </summary>
public sealed class PayMongoCheckoutExpiryService(
    HttpClient http,
    IPayMongoCredentialResolver credentials,
    IPaymentGateway gateway,
    ILogger<PayMongoCheckoutExpiryService> logger) : IPayMongoCheckoutExpiryService
{
    public async Task<Result<PaymentGatewayEvent>> ExpireAndVerifyAsync(
        string checkoutId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(checkoutId)
            || !checkoutId.StartsWith("cs_", StringComparison.Ordinal)
            || checkoutId.Length > 120
            || !checkoutId.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return Result<PaymentGatewayEvent>.Failure("Invalid checkout reference.", ResultStatus.Invalid);

        var account = await credentials.ResolveAsync(cancellationToken);
        if (!account.IsConfigured
            || !account.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal))
            return Result<PaymentGatewayEvent>.Failure(
                "Only checkouts belonging to the office's saved PayMongo Test account can be closed.",
                ResultStatus.Forbidden);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"checkout_sessions/{checkoutId}/expire");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{account.SecretKey}:")));
            using var response = await http.SendAsync(request, cancellationToken);
            // PayMongo returns HTTP 400 when the checkout was already expired, was paid, or has
            // a pending/processing payment. Re-read on 400; NEVER assume the payment failed.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest)
            {
                logger.LogWarning("PayMongo checkout expiry refused with HTTP {Status}.",
                    (int)response.StatusCode);
                return Result<PaymentGatewayEvent>.Failure(
                    "PayMongo could not close this checkout; its status remains unchanged.",
                    ResultStatus.UpstreamFailed);
            }

            var verification = await gateway.RetrievePaymentStatusAsync(checkoutId, cancellationToken);
            if (!verification.IsSuccess || verification.Value is null)
                return Result<PaymentGatewayEvent>.Failure(
                    "PayMongo's checkout state could not be confirmed; no local payment status was changed.",
                    ResultStatus.UpstreamFailed);

            if (!string.Equals(verification.Value.GatewayReference, checkoutId, StringComparison.Ordinal))
                return Result<PaymentGatewayEvent>.Failure(
                    "PayMongo returned a different checkout reference. No local status was changed.",
                    ResultStatus.UpstreamFailed);

            return verification;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PayMongo checkout expiry verification unavailable.");
            return Result<PaymentGatewayEvent>.Failure(
                "Unable to verify checkout expiry with PayMongo. No local status was changed.",
                ResultStatus.UpstreamFailed);
        }
    }
}
