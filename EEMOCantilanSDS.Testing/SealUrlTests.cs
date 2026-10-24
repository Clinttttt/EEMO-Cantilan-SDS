using EEMOCantilanSDS.Api.Controllers;
using Microsoft.AspNetCore.Http;

namespace EEMOCantilanSDS.Testing;

public sealed class SealUrlTests
{
    private static HttpRequest InternalApiRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("api", 8080);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        return context.Request;
    }

    [Fact]
    public void Internal_Docker_request_returns_browser_reachable_public_seal_url()
    {
        var result = SealUrl.From(InternalApiRequest(), "carrascal",
            "data:image/jpeg;base64,/9j/AA==", "https://api.stalltrack.site");

        Assert.StartsWith("https://api.stalltrack.site/api/municipalities/carrascal/seal?v=", result);
        Assert.DoesNotContain("api:8080", result);
    }

    [Fact]
    public void Different_tenants_keep_distinct_paths_and_image_versions()
    {
        const string baseUrl = "https://api.stalltrack.site";
        var request = InternalApiRequest();
        var carrascal = SealUrl.From(request, "carrascal", "data:image/jpeg;base64,AAAA", baseUrl);
        var madrid = SealUrl.From(request, "madrid", "data:image/jpeg;base64,BBBB", baseUrl);
        Assert.Contains("/carrascal/seal?v=", carrascal);
        Assert.Contains("/madrid/seal?v=", madrid);
        Assert.NotEqual(carrascal, madrid);
    }

    [Fact]
    public void Unconfigured_local_environment_preserves_request_origin()
    {
        var result = SealUrl.From(InternalApiRequest(), "carrascal",
            "data:image/jpeg;base64,AAAA");
        Assert.StartsWith("https://api:8080/api/municipalities/carrascal/seal?v=", result);
    }

    [Fact]
    public void Non_embedded_images_and_missing_seals_are_unchanged()
    {
        var request = InternalApiRequest();
        Assert.Null(SealUrl.From(request, "carrascal", null, "https://api.stalltrack.site"));
        Assert.Equal("/images/logo.png", SealUrl.From(request, "carrascal",
            "/images/logo.png", "https://api.stalltrack.site"));
    }
}
