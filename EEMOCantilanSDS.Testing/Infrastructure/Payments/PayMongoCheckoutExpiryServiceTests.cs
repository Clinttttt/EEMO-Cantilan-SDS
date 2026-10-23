using System.Net;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EEMOCantilanSDS.Testing.Infrastructure.Payments;

public class PayMongoCheckoutExpiryServiceTests
{
    private const string CheckoutId = "cs_test_checkout";

    private static (PayMongoCheckoutExpiryService service, Mock<IPaymentGateway> gateway, List<string> posts) Create(
        HttpStatusCode expireStatus = HttpStatusCode.OK, string key = "sk_test_only")
    {
        var credentials = new Mock<IPayMongoCredentialResolver>();
        credentials.Setup(x => x.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayMongoCredentials(key, null, null));
        var gateway = new Mock<IPaymentGateway>();
        var posts = new List<string>();
        var client = new HttpClient(new StubHttpHandler(req =>
        {
            posts.Add($"{req.Method} {req.RequestUri?.AbsolutePath}");
            return new HttpResponseMessage(expireStatus);
        })) { BaseAddress = new Uri("https://api.paymongo.com/v1/") };
        return (new PayMongoCheckoutExpiryService(client, credentials.Object, gateway.Object,
            NullLogger<PayMongoCheckoutExpiryService>.Instance), gateway, posts);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ProviderConfirmedExpiry_IsTrustedOnlyAfterFreshMatchingRead(HttpStatusCode responseCode)
    {
        var (service, gateway, posts) = Create(responseCode);
        gateway.Setup(x => x.RetrievePaymentStatusAsync(CheckoutId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(PaymentGatewayEventType.Expired, CheckoutId,
                    0m, null, null, null, "{}")));

        var result = await service.ExpireAndVerifyAsync(CheckoutId);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PaymentGatewayEventType.Expired, result.Value!.Type);
        Assert.Equal("POST /v1/checkout_sessions/cs_test_checkout/expire", Assert.Single(posts));
        gateway.Verify(x => x.RetrievePaymentStatusAsync(CheckoutId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CapturedProviderPayment_NeverReturnsExpired()
    {
        var (service, gateway, _) = Create(HttpStatusCode.BadRequest);
        gateway.Setup(x => x.RetrievePaymentStatusAsync(CheckoutId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(PaymentGatewayEventType.Paid, CheckoutId,
                    2400m, "pay_real", "qrph", DateTime.UtcNow, "{}")));
        var result = await service.ExpireAndVerifyAsync(CheckoutId);
        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentGatewayEventType.Paid, result.Value!.Type);
    }

    [Fact]
    public async Task MismatchedCheckoutIdentity_FailsClosed()
    {
        var (service, gateway, _) = Create();
        gateway.Setup(x => x.RetrievePaymentStatusAsync(CheckoutId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(PaymentGatewayEventType.Expired, "cs_another_checkout",
                    0m, null, null, null, "{}")));
        var result = await service.ExpireAndVerifyAsync(CheckoutId);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task LiveKeyAndFailedProviderRequest_DoNotAuthorizeExpiry()
    {
        var (liveService, liveGateway, livePosts) = Create(key: "sk_live_no_cleanup");
        Assert.False((await liveService.ExpireAndVerifyAsync(CheckoutId)).IsSuccess);
        Assert.Empty(livePosts);
        liveGateway.VerifyNoOtherCalls();

        var (failedService, failedGateway, failedPosts) = Create(HttpStatusCode.ServiceUnavailable);
        Assert.False((await failedService.ExpireAndVerifyAsync(CheckoutId)).IsSuccess);
        Assert.Single(failedPosts);
        failedGateway.VerifyNoOtherCalls();
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
