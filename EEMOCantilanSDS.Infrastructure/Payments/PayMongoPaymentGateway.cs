using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <summary>
/// PayMongo implementation of <see cref="IPaymentGateway"/> using hosted Checkout Sessions with
/// QR Ph as the current enabled live payment channel. The secret-key Basic-auth header is applied
/// PER-REQUEST from the tenant's resolved credentials (<see cref="IPayMongoCredentialResolver"/>), so each
/// LGU's payment hits its own PayMongo account; the default LGU (Cantilan) resolves to the global config,
/// keeping it byte-for-byte on the primary account. The webhook signing secret is read on demand.
/// </summary>
public sealed class PayMongoPaymentGateway(
    HttpClient httpClient,
    IConfiguration configuration,
    IPayMongoCredentialResolver credentialResolver,
    ILogger<PayMongoPaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "PayMongo";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Provider => ProviderName;

    public async Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        // PayMongo works in centavos (smallest unit). Convert pesos → integer centavos.
        var amountCentavos = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);

        var body = new
        {
            data = new
            {
                attributes = new
                {
                    send_email_receipt = false,
                    show_description = true,
                    show_line_items = true,
                    description = request.Description,
                    line_items = new[]
                    {
                        new
                        {
                            currency = "PHP",
                            amount = amountCentavos,
                            name = request.Description,
                            quantity = 1
                        }
                    },
                    payment_method_types = new[] { "qrph" },
                    reference_number = request.Reference,
                    success_url = request.SuccessUrl,
                    cancel_url = request.CancelUrl,
                    billing = string.IsNullOrWhiteSpace(request.CustomerName) && string.IsNullOrWhiteSpace(request.CustomerEmail)
                        ? null
                        : new { name = request.CustomerName, email = request.CustomerEmail }
                }
            }
        };

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            var credentials = await credentialResolver.ResolveAsync(cancellationToken);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "checkout_sessions")
            {
                Content = content,
                Headers = { Authorization = BasicAuth(credentials.SecretKey) }
            };

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("PayMongo checkout session creation failed ({Status}): {Error}",
                    (int)response.StatusCode, SummarizePayMongoError(payload));
                return Result<CheckoutSessionResult>.Failure(
                    $"Payment provider rejected the request ({(int)response.StatusCode}).", 502);
            }

            using var doc = JsonDocument.Parse(payload);
            var data = doc.RootElement.GetProperty("data");
            var id = data.GetProperty("id").GetString();
            var checkoutUrl = data.GetProperty("attributes").GetProperty("checkout_url").GetString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(checkoutUrl))
            {
                logger.LogError("PayMongo checkout session response missing id/checkout_url (status {Status}).", (int)response.StatusCode);
                return Result<CheckoutSessionResult>.Failure("Payment provider returned an incomplete response.", 502);
            }

            return Result<CheckoutSessionResult>.Success(new CheckoutSessionResult(checkoutUrl, id, ProviderName));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "PayMongo checkout session creation threw.");
            return Result<CheckoutSessionResult>.Failure("Unable to reach the payment provider.", 502);
        }
    }

    public bool VerifyWebhookSignature(string payload, string signatureHeader) =>
        VerifyWebhookSignature(payload, signatureHeader, configuration["PayMongo:WebhookSecret"] ?? string.Empty);

    public bool VerifyWebhookSignature(string payload, string signatureHeader, string webhookSecret)
    {
        var secret = webhookSecret;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(payload))
            return false;

        // PayMongo signature header format: "t=<timestamp>,te=<test_sig>,li=<live_sig>".
        string? timestamp = null, testSig = null, liveSig = null;
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0].Trim())
            {
                case "t": timestamp = kv[1].Trim(); break;
                case "te": testSig = kv[1].Trim(); break;
                case "li": liveSig = kv[1].Trim(); break;
            }
        }

        if (string.IsNullOrWhiteSpace(timestamp))
            return false;

        // Reject stale/future timestamps to blunt replay (settlement is also idempotent). The window is
        // generous and configurable so legitimate provider retries within it still verify; a webhook that
        // arrives beyond the window settles via the reconciliation fallback (payor return / staff reconcile).
        if (!long.TryParse(timestamp, out var epochSeconds))
            return false;
        var toleranceMinutes = int.TryParse(configuration["PayMongo:WebhookToleranceMinutes"], out var configured) && configured > 0
            ? configured
            : 720; // 12h default
        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        if (age > TimeSpan.FromMinutes(toleranceMinutes) || age < TimeSpan.FromMinutes(-toleranceMinutes))
            return false;

        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        return SignatureMatches(computed, testSig) || SignatureMatches(computed, liveSig);
    }

    public Result<PaymentGatewayEvent> ParseEvent(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Result<PaymentGatewayEvent>.Failure("Empty webhook payload.");

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var attributes = doc.RootElement.GetProperty("data").GetProperty("attributes");
            var eventName = attributes.GetProperty("type").GetString() ?? string.Empty;
            var resource = attributes.GetProperty("data");
            var resourceId = resource.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            var resourceAttrs = resource.TryGetProperty("attributes", out var raEl) ? raEl : default;

            var type = eventName switch
            {
                "checkout_session.payment.paid" or "payment.paid" => PaymentGatewayEventType.Paid,
                "payment.failed" => PaymentGatewayEventType.Failed,
                "checkout_session.expired" => PaymentGatewayEventType.Expired,
                _ => PaymentGatewayEventType.Unknown
            };

            // For checkout-session events the actual payment lives in attributes.payments[0];
            // for direct payment events the amount/id are on the resource itself.
            JsonElement paymentAttrs = resourceAttrs;
            string? paymentId = resourceId.StartsWith("pay_", StringComparison.OrdinalIgnoreCase) ? resourceId : null;

            if (resourceAttrs.ValueKind == JsonValueKind.Object
                && resourceAttrs.TryGetProperty("payments", out var payments)
                && payments.ValueKind == JsonValueKind.Array
                && payments.GetArrayLength() > 0)
            {
                var firstPayment = payments[0];
                if (firstPayment.TryGetProperty("id", out var payIdEl))
                    paymentId = payIdEl.GetString();
                if (firstPayment.TryGetProperty("attributes", out var payAttrsEl))
                    paymentAttrs = payAttrsEl;
            }

            decimal amount = 0m;
            if (paymentAttrs.ValueKind == JsonValueKind.Object
                && paymentAttrs.TryGetProperty("amount", out var amountEl)
                && amountEl.ValueKind == JsonValueKind.Number)
            {
                amount = amountEl.GetInt64() / 100m;
            }

            string? method = null;
            if (paymentAttrs.ValueKind == JsonValueKind.Object
                && paymentAttrs.TryGetProperty("source", out var sourceEl)
                && sourceEl.ValueKind == JsonValueKind.Object
                && sourceEl.TryGetProperty("type", out var methodEl))
            {
                method = methodEl.GetString();
            }

            DateTime? paidAt = null;
            if (paymentAttrs.ValueKind == JsonValueKind.Object
                && paymentAttrs.TryGetProperty("paid_at", out var paidAtEl)
                && paidAtEl.ValueKind == JsonValueKind.Number)
            {
                paidAt = DateTimeOffset.FromUnixTimeSeconds(paidAtEl.GetInt64()).UtcDateTime;
            }

            return Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(type, resourceId, amount, paymentId, method, paidAt, payload));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to parse PayMongo webhook payload.");
            return Result<PaymentGatewayEvent>.Failure("Malformed webhook payload.");
        }
    }

    /// <summary>
    /// Reconciliation fallback: retrieves the checkout session directly from PayMongo and maps it to a
    /// normalized event. A session whose <c>payments[]</c> contains a <c>paid</c> payment is reported as
    /// Paid (with that payment's id/amount/method/paid_at); an <c>expired</c> session as Expired;
    /// everything else (still active/unpaid) as Unknown. Read-only — never mutates the provider.
    /// </summary>
    public async Task<Result<PaymentGatewayEvent>> RetrievePaymentStatusAsync(
        string gatewayReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayReference))
            return Result<PaymentGatewayEvent>.Failure("Missing gateway reference.", 400);

        try
        {
            var credentials = await credentialResolver.ResolveAsync(cancellationToken);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"checkout_sessions/{gatewayReference}")
            {
                Headers = { Authorization = BasicAuth(credentials.SecretKey) }
            };
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("PayMongo checkout session retrieve failed ({Status}): {Payload}",
                    (int)response.StatusCode, payload);
                return Result<PaymentGatewayEvent>.Failure(
                    $"Payment provider returned {(int)response.StatusCode} while verifying the payment.", 502);
            }

            using var doc = JsonDocument.Parse(payload);
            var data = doc.RootElement.GetProperty("data");
            var sessionId = data.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? gatewayReference : gatewayReference;
            var attrs = data.GetProperty("attributes");

            // Look for a settled payment in the session's payments[] array.
            if (attrs.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
            {
                foreach (var payment in payments.EnumerateArray())
                {
                    if (!payment.TryGetProperty("attributes", out var pa) || pa.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!pa.TryGetProperty("status", out var statusEl)
                        || !string.Equals(statusEl.GetString(), "paid", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var paymentId = payment.TryGetProperty("id", out var payIdEl) ? payIdEl.GetString() : null;

                    decimal amount = 0m;
                    if (pa.TryGetProperty("amount", out var amtEl) && amtEl.ValueKind == JsonValueKind.Number)
                        amount = amtEl.GetInt64() / 100m;

                    string? method = null;
                    if (pa.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.Object
                        && srcEl.TryGetProperty("type", out var methodEl))
                        method = methodEl.GetString();

                    DateTime? paidAt = null;
                    if (pa.TryGetProperty("paid_at", out var paidAtEl) && paidAtEl.ValueKind == JsonValueKind.Number)
                        paidAt = DateTimeOffset.FromUnixTimeSeconds(paidAtEl.GetInt64()).UtcDateTime;

                    return Result<PaymentGatewayEvent>.Success(
                        new PaymentGatewayEvent(PaymentGatewayEventType.Paid, sessionId, amount, paymentId, method, paidAt, payload));
                }
            }

            // No paid payment — expired session vs still-pending.
            var sessionStatus = attrs.TryGetProperty("status", out var sStatusEl) ? sStatusEl.GetString() : null;
            var type = string.Equals(sessionStatus, "expired", StringComparison.OrdinalIgnoreCase)
                ? PaymentGatewayEventType.Expired
                : PaymentGatewayEventType.Unknown;

            return Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(type, sessionId, 0m, null, null, null, payload));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "PayMongo checkout session retrieve threw.");
            return Result<PaymentGatewayEvent>.Failure("Unable to reach the payment provider.", 502);
        }
    }

    private static AuthenticationHeaderValue BasicAuth(string secretKey) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:")));

    // PayMongo error bodies can echo billing details; log only the provider's error code/detail —
    // never the raw payload — so logs never carry payor PII.
    private static string SummarizePayMongoError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var err in errors.EnumerateArray())
                {
                    var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
                    var detail = err.TryGetProperty("detail", out var d) ? d.GetString() : null;
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(string.IsNullOrWhiteSpace(code) ? "error" : code);
                    if (!string.IsNullOrWhiteSpace(detail)) sb.Append(": ").Append(detail);
                }
                if (sb.Length > 0) return sb.ToString();
            }
        }
        catch (JsonException) { /* fall through to redacted note */ }
        return "(error body redacted)";
    }


    private static bool SignatureMatches(string computedHex, string? providedHex)
    {
        if (string.IsNullOrWhiteSpace(providedHex) || computedHex.Length != providedHex.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computedHex),
            Encoding.ASCII.GetBytes(providedHex.ToLower(CultureInfo.InvariantCulture)));
    }
}
