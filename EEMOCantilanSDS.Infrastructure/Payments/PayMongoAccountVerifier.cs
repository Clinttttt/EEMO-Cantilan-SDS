using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <inheritdoc cref="IPayMongoAccountVerifier"/>
public sealed class PayMongoAccountVerifier(HttpClient httpClient, ILogger<PayMongoAccountVerifier> logger)
    : IPayMongoAccountVerifier
{
    public async Task<Result<bool>> VerifyAsync(string secretKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            return Result<bool>.Failure("Enter the secret key first.", ResultStatus.Invalid);

        // Listing webhooks is the cheapest authenticated call that proves the key belongs to a real account, and it asks
        // nothing of the merchant's money. A key that cannot read its own webhooks cannot register one either, which is the
        // next thing this screen will want to do.
        using var request = new HttpRequestMessage(HttpMethod.Get, "webhooks")
        {
            Headers = { Authorization = BasicAuth(secretKey.Trim()) }
        };

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return Result<bool>.Success(true);

            // Said apart from every other failure, because this is the one the office can fix by pasting the right key.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result<bool>.Failure(
                    "PayMongo did not accept this secret key. Check that it was copied in full from your own account.",
                    ResultStatus.Invalid);

            logger.LogWarning("PayMongo credential check returned {Status}.", (int)response.StatusCode);
            return Result<bool>.Failure(
                $"PayMongo answered {(int)response.StatusCode} while checking the key. Try again shortly.",
                ResultStatus.UpstreamFailed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // NOT reported as a bad key. Telling an office its key is wrong because the network faltered is how a correct
            // key gets replaced with a guess.
            logger.LogWarning(ex, "PayMongo credential check could not reach the provider.");
            return Result<bool>.Failure(
                "Could not reach PayMongo to check the key. This does not mean the key is wrong - try again shortly.",
                ResultStatus.UpstreamFailed);
        }
    }

    public async Task<Result<PayMongoWebhookRegistration>> EnsureWebhookAsync(
        string secretKey,
        string webhookUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            return Result<PayMongoWebhookRegistration>.Failure("Enter the secret key first.", ResultStatus.Invalid);
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return Result<PayMongoWebhookRegistration>.Failure("No webhook address to register.", ResultStatus.Invalid);

        var key = secretKey.Trim();
        var target = webhookUrl.Trim();

        try
        {
            // Look before creating. An office that saves its keys twice must not end up with two webhooks for the same
            // address: PayMongo would hold both, deliver to both, and nobody could tell which secret signs what.
            var existing = await FindByUrlAsync(key, target, cancellationToken);

            if (existing is { } found)
            {
                var reEnabled = false;

                if (!string.Equals(found.Status, "enabled", StringComparison.OrdinalIgnoreCase))
                {
                    // PayMongo disables a webhook after repeated delivery failures, so finding one switched off is normal
                    // rather than exceptional - and leaving it off would mean nothing ever confirms a payment again.
                    reEnabled = await EnableAsync(key, found.Id, cancellationToken);
                }

                return Result<PayMongoWebhookRegistration>.Success(new PayMongoWebhookRegistration(
                    found.Id,
                    // Deliberately not carried over. PayMongo reveals a secret when a webhook is CREATED; what is returned
                    // when listing is not to be relied on, and handing back an empty one must never overwrite a working
                    // stored secret.
                    SigningSecret: found.SecretKey,
                    AlreadyExisted: true,
                    WasReEnabled: reEnabled));
            }

            return await CreateAsync(key, target, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayMongo webhook provisioning could not complete.");
            return Result<PayMongoWebhookRegistration>.Failure(
                "Could not reach PayMongo to register the webhook. The secret key is saved; add the webhook manually, or try again.",
                ResultStatus.UpstreamFailed);
        }
    }

    /// <summary>The webhook already registered for this exact address, or null when there is none.</summary>
    private async Task<PayMongoWebhookRecord?> FindByUrlAsync(string key, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "webhooks")
        {
            Headers = { Authorization = BasicAuth(key) }
        };

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(payload);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("attributes", out var attributes)) continue;

            var itemUrl = attributes.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (!string.Equals(itemUrl?.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) continue;

            return new PayMongoWebhookRecord(
                item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                attributes.TryGetProperty("status", out var s) ? s.GetString() : null,
                attributes.TryGetProperty("secret_key", out var sk) ? sk.GetString() : null);
        }

        return null;
    }

    private async Task<bool> EnableAsync(string key, string webhookId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"webhooks/{webhookId}/enable")
        {
            Headers = { Authorization = BasicAuth(key) }
        };

        using var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return true;

        logger.LogWarning("PayMongo refused to re-enable webhook {WebhookId} ({Status}).",
            webhookId, (int)response.StatusCode);
        return false;
    }

    private async Task<Result<PayMongoWebhookRegistration>> CreateAsync(string key, string url, CancellationToken ct)
    {
        // Exactly the events this system understands. Subscribing to more would mean accepting notifications nothing acts
        // on, and PayMongo counts a delivery we cannot answer against the webhook's health.
        var body = new
        {
            data = new
            {
                attributes = new
                {
                    url,
                    events = new[] { "checkout_session.payment.paid", "payment.paid", "payment.failed" }
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "webhooks")
        {
            Content = content,
            Headers = { Authorization = BasicAuth(key) }
        };

        using var response = await httpClient.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PayMongo webhook creation failed ({Status}).", (int)response.StatusCode);
            return Result<PayMongoWebhookRegistration>.Failure(
                $"PayMongo refused to register the webhook ({(int)response.StatusCode}). The secret key is saved; add the webhook manually.",
                ResultStatus.UpstreamFailed);
        }

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("data", out var created)
            || !created.TryGetProperty("attributes", out var attributes))
        {
            return Result<PayMongoWebhookRegistration>.Failure(
                "PayMongo registered the webhook but did not describe it. Check Developers -> Webhooks in your dashboard.",
                ResultStatus.UpstreamFailed);
        }

        var webhookId = created.TryGetProperty("id", out var id) ? id.GetString() : null;
        var secret = attributes.TryGetProperty("secret_key", out var sk) ? sk.GetString() : null;

        if (string.IsNullOrWhiteSpace(webhookId))
        {
            return Result<PayMongoWebhookRegistration>.Failure(
                "PayMongo registered the webhook without returning its id. Check Developers -> Webhooks in your dashboard.",
                ResultStatus.UpstreamFailed);
        }

        return Result<PayMongoWebhookRegistration>.Success(
            new PayMongoWebhookRegistration(webhookId!, secret, AlreadyExisted: false, WasReEnabled: false));
    }

    /// <summary>What PayMongo says about one registered webhook.</summary>
    private sealed record PayMongoWebhookRecord(string Id, string? Status, string? SecretKey);

    private static AuthenticationHeaderValue BasicAuth(string secretKey) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(secretKey + ":")));
}
