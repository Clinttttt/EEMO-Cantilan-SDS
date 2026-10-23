using System.Net;
using System.Net.Http;
using System.Text;
using EEMOCantilanSDS.Infrastructure.Payments;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EEMOCantilanSDS.Testing.Infrastructure.Payments;

public class PayMongoWebhookProvisioningSafetyTests
{
    private const string Url = "https://api.stalltrack.site/api/onlinepayments/webhook/tenant-a";
    private static string Webhook(string status, string[] events) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            data = new[] { new { id = "hook_existing", attributes = new
            {
                url = Url, status, events, secret_key = "whsk_fake"
            } } }
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (PayMongoAccountVerifier verifier, List<string> calls) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var calls = new List<string>();
        var handler = new Handler(req =>
        {
            calls.Add($"{req.Method} {req.RequestUri?.AbsolutePath}");
            return respond(req);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.paymongo.com/v1/") };
        return (new PayMongoAccountVerifier(client, NullLogger<PayMongoAccountVerifier>.Instance), calls);
    }

    [Fact]
    public async Task ExistingWebhookMissingPaidEvent_IsNotReportedAsReady_AndNotDuplicated()
    {
        var (verifier, calls) = Build(req => Json(HttpStatusCode.OK,
            Webhook("enabled", ["payment.failed"])));
        var result = await verifier.EnsureWebhookAsync("sk_test_fake", Url);
        Assert.False(result.IsSuccess);
        Assert.Contains("checkout_session.payment.paid", result.Error);
        Assert.Single(calls);
    }

    [Fact]
    public async Task ReenableFailure_FailsClosedEvenIfWebhookHasSigningSecret()
    {
        var (verifier, calls) = Build(req =>
            req.Method == HttpMethod.Get ? Json(HttpStatusCode.OK,
                Webhook("disabled", ["checkout_session.payment.paid"]))
                : Json(HttpStatusCode.Forbidden, "{}"));
        var result = await verifier.EnsureWebhookAsync("sk_test_fake", Url);
        Assert.False(result.IsSuccess);
        Assert.Contains("enable", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task ListingFailure_DoesNotCreateSecondWebhook()
    {
        var (verifier, calls) = Build(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var result = await verifier.EnsureWebhookAsync("sk_test_fake", Url);
        Assert.False(result.IsSuccess);
        Assert.Single(calls);
    }

    [Fact]
    public async Task ExistingEnabledWebhookWithCorrectEvent_IsReused()
    {
        var (verifier, calls) = Build(_ => Json(HttpStatusCode.OK,
            Webhook("enabled", ["checkout_session.payment.paid", "payment.paid"])));
        var result = await verifier.EnsureWebhookAsync("sk_test_fake", Url);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("hook_existing", result.Value!.WebhookId);
        Assert.True(result.Value.AlreadyExisted);
        Assert.Single(calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handle(request));
    }
}

public class PayMongoPaidAttemptParsingTests
{
    private static PayMongoPaymentGateway Gateway() => new(new HttpClient(),
        new ConfigurationBuilder().Build(), Mock.Of<IPayMongoCredentialResolver>(),
        NullLogger<PayMongoPaymentGateway>.Instance);

    private static string Payload(string payments)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(payments);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            data = new { attributes = new
            {
                type = "checkout_session.payment.paid",
                data = new { id = "cs_test_123", attributes = new { payments = doc.RootElement } }
            } }
        });
    }

    [Fact]
    public void PaidAttemptNotFirst_UsesOnlyActuallyPaidPayment()
    {
        var payload = Payload("""
            [{"id":"pay_failed","attributes":{"status":"failed","amount":10100}},
             {"id":"pay_paid","attributes":{"status":"paid","amount":12500,"source":{"type":"qrph"}}}]
            """);
        var result = Gateway().ParseEvent(payload);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PaymentGatewayEventType.Paid, result.Value!.Type);
        Assert.Equal("cs_test_123", result.Value.GatewayReference);
        Assert.Equal("pay_paid", result.Value.PaymentId);
        Assert.Equal(125m, result.Value.Amount);
    }

    [Fact]
    public void CheckoutPaidNotificationWithNoPaidAttempt_FailsClosed()
    {
        var payload = Payload("""
            [{"id":"pay_failed","attributes":{"status":"failed","amount":12500}}]
            """);
        var result = Gateway().ParseEvent(payload);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void CheckoutNotificationWithoutPaymentEvidence_FailsClosed()
    {
        var result = Gateway().ParseEvent(Payload("[]"));
        Assert.False(result.IsSuccess);
    }
}
