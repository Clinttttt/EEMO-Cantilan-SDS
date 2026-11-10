using System.Net;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EEMOCantilanSDS.Testing.Infrastructure.Payments;

/// <summary>What a read of a checkout session means. A session stays open after a failed attempt, so Failed is not the same as Expired.</summary>
public class PayMongoCheckoutStatusTests
{
    private static async Task<PaymentGatewayEventType> Read(string sessionStatus, params string[] attempts)
    {
        var payments = string.Join(",", attempts.Select((status, i) =>
            $$$"""{"id":"pay_{{{i}}}","attributes":{"status":"{{{status}}}","amount":10000}}"""));
        var body = "{\"data\":{\"id\":\"cs_1\",\"attributes\":{\"status\":\"" + sessionStatus + "\",\"payments\":[" + payments + "]}}}";

        var credentials = new Mock<IPayMongoCredentialResolver>();
        credentials.Setup(c => c.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayMongoCredentials("sk_test_key", null, null));
        var client = new HttpClient(new StubHandler(body)) { BaseAddress = new Uri("https://api.paymongo.test/v1/") };
        var gateway = new PayMongoPaymentGateway(client, new ConfigurationBuilder().Build(), credentials.Object,
            NullLogger<PayMongoPaymentGateway>.Instance);

        var result = await gateway.RetrievePaymentStatusAsync("cs_1");
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Type;
    }

    [Fact]
    public async Task EveryAttemptFailed_ReadsAsFailed() =>
        Assert.Equal(PaymentGatewayEventType.Failed, await Read("active", "failed", "failed"));

    [Fact]
    public async Task AFailedAttemptAndAnOngoingOne_IsNotAFailure() =>
        Assert.Equal(PaymentGatewayEventType.Unknown, await Read("active", "failed", "pending"));

    [Fact]
    public async Task NoAttemptsYet_IsNotAFailure() =>
        Assert.Equal(PaymentGatewayEventType.Unknown, await Read("active"));

    [Fact]
    public async Task APaidAttempt_WinsOverEarlierFailures() =>
        Assert.Equal(PaymentGatewayEventType.Paid, await Read("active", "failed", "paid"));

    // The Head's Test-checkout cleanup only counts Expired as closed, so a closed session must read as Expired whatever its attempts did.
    [Fact]
    public async Task AClosedSession_ReadsAsExpired_EvenAfterFailedAttempts() =>
        Assert.Equal(PaymentGatewayEventType.Expired, await Read("expired", "failed"));

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
