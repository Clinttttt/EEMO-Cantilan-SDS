using EEMOCantilanSDS.Infrastructure.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Testing.Infrastructure.Payments;

/// <summary>
/// Where a payor lands after checkout.
///
/// <para>
/// Two payor portals exist while the Angular one takes over from the Blazor one, and each has to return to itself: a
/// payor who started on payor.stalltrack.site cannot be dropped on the other portal's screen, where their session does
/// not exist. The browser says which portal they are on, and the Blazor portal, which calls this API server-to-server,
/// says nothing and so keeps the configured address.
/// </para>
///
/// <para>
/// The origin is never trusted as given. These pin that: an address off the list is refused, because a caller who could
/// name the return address could send a payor to a page of their own directly after paying.
/// </para>
/// </summary>
public class OnlinePaymentUrlBuilderReturnOriginTests
{
    private const string Configured = "https://console.stalltrack.site";
    private const string Payor = "https://payor.stalltrack.site";

    [Fact]
    public void AnOriginOnTheListIsWhereThatPayorReturns()
    {
        var builder = Build(origin: Payor);

        Assert.Equal($"{Payor}/payor/payment/success?ref=OP-1", builder.BuildSuccessUrl("OP-1"));
        Assert.Equal($"{Payor}/payor/payment/cancelled?ref=OP-1", builder.BuildCancelUrl("OP-1"));
    }

    [Fact]
    public void NoOriginMeansTheConfiguredPortal()
    {
        // The Blazor portal reaches this API server-to-server, so there is no Origin header and nothing to configure
        // for it. It must keep returning exactly where it always did.
        var builder = Build(origin: null);

        Assert.Equal($"{Configured}/payor/payment/success?ref=OP-2", builder.BuildSuccessUrl("OP-2"));
    }

    [Theory]
    [InlineData("https://payor.stalltrack.site.evil.test")]
    [InlineData("https://evil.test")]
    [InlineData("http://payor.stalltrack.site")]
    public void AnOriginOffTheListIsRefused(string origin)
    {
        // Including a look-alike host and the same host over plain http. Anything not matched falls back to the
        // configured portal rather than being honoured.
        var builder = Build(origin);

        Assert.Equal($"{Configured}/payor/payment/success?ref=OP-3", builder.BuildSuccessUrl("OP-3"));
    }

    [Fact]
    public void ATrailingSlashOnEitherSideStillMatches()
    {
        // Browsers send an origin without a trailing slash; a configured value may carry one. A mismatch there would
        // quietly send every payor to the other portal.
        var builder = Build(origin: $"{Payor}/", allowed: $"{Payor}/");

        Assert.Equal($"{Payor}/payor/payment/success?ref=OP-4", builder.BuildSuccessUrl("OP-4"));
    }

    private static OnlinePaymentUrlBuilder Build(string? origin, string allowed = Payor)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OnlinePayments:PortalBaseUrl"] = Configured,
                ["OnlinePayments:AllowedReturnOrigins:0"] = allowed,
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
            })
            .Build();

        var context = new DefaultHttpContext();
        if (origin is not null) context.Request.Headers["Origin"] = origin;

        return new OnlinePaymentUrlBuilder(configuration, new HttpContextAccessor { HttpContext = context });
    }
}
