using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Services;
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

    private static AuthenticationHeaderValue BasicAuth(string secretKey) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(secretKey + ":")));
}
