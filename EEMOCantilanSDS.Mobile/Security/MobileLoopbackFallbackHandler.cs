using System.Net.Http.Headers;

namespace EEMOCantilanSDS.Mobile.Security;

public sealed class MobileLoopbackFallbackHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsDevLoopbackRequest(request.RequestUri))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var retryRequest = await CloneRequestAsync(request, cancellationToken);

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException) when (retryRequest is not null)
        {
            retryRequest.RequestUri = SwapLoopbackHost(retryRequest.RequestUri);
            return await base.SendAsync(retryRequest, cancellationToken);
        }
    }

    private static bool IsDevLoopbackRequest(Uri? uri) =>
        uri is not null
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Port == 5117
        && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1");

    private static Uri? SwapLoopbackHost(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Host = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                ? "127.0.0.1"
                : "localhost"
        };

        return builder.Uri;
    }

    private static async Task<HttpRequestMessage?> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content.Headers.ContentType is MediaTypeHeaderValue contentType)
            {
                clone.Content.Headers.ContentType = contentType;
            }
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}
