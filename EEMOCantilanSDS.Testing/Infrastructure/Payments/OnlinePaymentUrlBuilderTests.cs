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
