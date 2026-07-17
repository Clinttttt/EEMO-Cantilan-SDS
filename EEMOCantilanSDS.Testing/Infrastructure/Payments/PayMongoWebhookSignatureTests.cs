using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Infrastructure.Payments;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Infrastructure.Payments
{
    /// <summary>
    /// #8 hardening — the PayMongo webhook signature is authentic AND fresh: a correctly-signed but stale
    /// timestamp is rejected (replay), while a fresh one within the tolerance verifies.
    /// </summary>
    public class PayMongoWebhookSignatureTests
    {
        private const string Secret = "whsec_test_secret";
        private const string Payload = "{\"data\":{\"attributes\":{\"type\":\"payment.paid\"}}}";

        private static PayMongoPaymentGateway Gateway()
        {
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["PayMongo:WebhookSecret"]).Returns(Secret);
            config.Setup(c => c["PayMongo:WebhookToleranceMinutes"]).Returns((string?)null); // default (12h)
            return new PayMongoPaymentGateway(new HttpClient(), config.Object, new Mock<IPayMongoCredentialResolver>().Object, NullLogger<PayMongoPaymentGateway>.Instance);
        }

        private static string Header(long epochSeconds)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{epochSeconds}.{Payload}"))).ToLowerInvariant();
            return $"t={epochSeconds},li={sig}";
        }

        [Fact]
        public void FreshSignature_Verifies()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert.True(Gateway().VerifyWebhookSignature(Payload, Header(now)));
        }

        [Fact]
        public void StaleSignature_IsRejected_EvenWhenOtherwiseValid()
        {
            // 13h old — beyond the 12h default tolerance. HMAC is valid, so this proves the freshness check.
            var stale = DateTimeOffset.UtcNow.AddHours(-13).ToUnixTimeSeconds();
            Assert.False(Gateway().VerifyWebhookSignature(Payload, Header(stale)));
        }

        [Fact]
        public void WrongSecret_IsRejected()
        {
            using var wrong = new HMACSHA256(Encoding.UTF8.GetBytes("whsec_wrong"));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var badSig = Convert.ToHexString(wrong.ComputeHash(Encoding.UTF8.GetBytes($"{now}.{Payload}"))).ToLowerInvariant();
            Assert.False(Gateway().VerifyWebhookSignature(Payload, $"t={now},li={badSig}"));
        }
    }
}
