using EEMOCantilanSDS.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EEMOCantilanSDS.Testing.Infrastructure.Payments;

/// <summary>
/// The portal base URL is what PayMongo redirects payors back to after checkout. A localhost value
/// leaking into a deployed environment would strand payors (and silently defeat the on-return
/// reconciliation), so the builder is fail-closed: unset or localhost-outside-Development must throw.
/// </summary>
public class OnlinePaymentUrlBuilderTests
{
    private static OnlinePaymentUrlBuilder Build(string? portalBaseUrl, string? environment)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["OnlinePayments:PortalBaseUrl"]).Returns(portalBaseUrl);
        config.Setup(c => c["ASPNETCORE_ENVIRONMENT"]).Returns(environment);
        return new OnlinePaymentUrlBuilder(config.Object);
    }

    [Fact]
    public void Development_LocalhostPortal_IsAllowed()
    {
        var builder = Build("https://localhost:7167", "Development");

        Assert.Equal(
            "https://localhost:7167/payor/payment/success?ref=EEMO-OP-1",
            builder.BuildSuccessUrl("EEMO-OP-1"));
        Assert.Equal(
            "https://localhost:7167/payor/payment/cancelled?ref=EEMO-OP-1",
            builder.BuildCancelUrl("EEMO-OP-1"));
    }

    [Fact]
    public void Production_LocalhostPortal_Throws()
    {
        var builder = Build("https://localhost:7167", "Production");

        Assert.Throws<InvalidOperationException>(() => builder.BuildSuccessUrl("EEMO-OP-1"));
        Assert.Throws<InvalidOperationException>(() => builder.BuildCancelUrl("EEMO-OP-1"));
    }

    [Fact]
    public void Production_LoopbackIp_Throws()
    {
        var builder = Build("http://127.0.0.1:5198", "Production");

        Assert.Throws<InvalidOperationException>(() => builder.BuildSuccessUrl("EEMO-OP-1"));
    }

    [Fact]
    public void UnknownEnvironment_LocalhostPortal_Throws_FailClosed()
    {
        // No ASPNETCORE_ENVIRONMENT set → treated as Production → the guard must still fire.
        var builder = Build("https://localhost:7167", environment: null);

        Assert.Throws<InvalidOperationException>(() => builder.BuildSuccessUrl("EEMO-OP-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPortalBaseUrl_Throws(string? portalBaseUrl)
    {
        var builder = Build(portalBaseUrl, "Development");

        Assert.Throws<InvalidOperationException>(() => builder.BuildSuccessUrl("EEMO-OP-1"));
    }

    [Fact]
    public void Production_PublicPortal_BuildsTrimsAndEscapes()
    {
        // Trailing slash trimmed; the reference is URL-escaped.
        var builder = Build("https://eemo.stalltrack.site/", "Production");

        Assert.Equal(
            "https://eemo.stalltrack.site/payor/payment/success?ref=EEMO-OP%2F1",
            builder.BuildSuccessUrl("EEMO-OP/1"));
        Assert.Equal(
            "https://eemo.stalltrack.site/payor/payment/cancelled?ref=EEMO-OP%2F1",
            builder.BuildCancelUrl("EEMO-OP/1"));
    }
}


/// <summary>
/// The webhook address PayMongo is asked to call, which is a different address from the payor's return URL and must not be
/// confused with it: the return URL is the portal, this is the API.
///
/// <para>
/// Two things have to hold. It must carry the LGU's own tenant code, because the tenant-less endpoint verifies against the
/// platform configuration - the DEFAULT municipality's signing secret - so any other LGU pointed at it would have every
/// notification refused. And it must be https, because PayMongo will not call an http address.
/// </para>
/// </summary>
public class OnlinePaymentWebhookUrlTests
{
    private static OnlinePaymentUrlBuilder Build(
        string? webhookBaseUrl, string? environment, string? requestScheme = null, string? requestHost = null)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["OnlinePayments:WebhookBaseUrl"]).Returns(webhookBaseUrl);
        config.Setup(c => c["ASPNETCORE_ENVIRONMENT"]).Returns(environment);

        Microsoft.AspNetCore.Http.IHttpContextAccessor? accessor = null;
        if (requestHost is not null)
        {
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            ctx.Request.Scheme = requestScheme ?? "http";
            ctx.Request.Host = new Microsoft.AspNetCore.Http.HostString(requestHost);

            var mock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            mock.SetupGet(a => a.HttpContext).Returns(ctx);
            accessor = mock.Object;
        }

        return new OnlinePaymentUrlBuilder(config.Object, accessor);
    }

    [Fact]
    public void TheAddressCarriesTheLGUsOwnTenantCode()
    {
        var builder = Build("https://api.stalltrack.site", "Production");

        Assert.Equal("https://api.stalltrack.site/api/onlinepayments/webhook/madrid",
            builder.BuildWebhookUrl("madrid"));
    }

    [Fact]
    public void WithoutATenantCodeItREFUSESToBuildOne()
    {
        // Silently producing the tenant-less endpoint would hand one LGU an address verified against another's secret.
        var builder = Build("https://api.stalltrack.site", "Production");

        Assert.Throws<InvalidOperationException>(() => builder.BuildWebhookUrl(" "));
    }

    [Fact]
    public void APLAINHTTPRequestStillYieldsAnHttpsAddress()
    {
        // The defect the office saw: the portal reaches this API server-to-server, so the scheme on the request is not
        // necessarily the public one, and http was shown as the address to paste into PayMongo.
        var builder = Build(webhookBaseUrl: null, environment: "Production",
            requestScheme: "http", requestHost: "api.stalltrack.site");

        Assert.Equal("https://api.stalltrack.site/api/onlinepayments/webhook/madrid",
            builder.BuildWebhookUrl("madrid"));
    }

    [Fact]
    public void ConfigurationWinsOverTheRequestsOwnOrigin()
    {
        var builder = Build("https://api.stalltrack.site", "Production",
            requestScheme: "https", requestHost: "internal.example");

        Assert.StartsWith("https://api.stalltrack.site/", builder.BuildWebhookUrl("madrid"));
    }

    [Fact]
    public void ALocalhostAddressIsRefusedOutsideDevelopment()
    {
        // A webhook registered against localhost is registered against nothing, and PayMongo would report it failing
        // forever - the same fail-closed rule the portal URL beside it follows.
        var builder = Build("https://localhost:5001", "Production");

        Assert.Throws<InvalidOperationException>(() => builder.BuildWebhookUrl("madrid"));
    }

    [Fact]
    public void ALocalhostAddressIsAllowedInDevelopment()
    {
        var builder = Build(webhookBaseUrl: null, environment: "Development",
            requestScheme: "http", requestHost: "localhost:5099");

        Assert.Equal("http://localhost:5099/api/onlinepayments/webhook/madrid",
            builder.BuildWebhookUrl("madrid"));
    }
}
