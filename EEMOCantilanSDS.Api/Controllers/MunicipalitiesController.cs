using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetCurrentMunicipalityBranding;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalities;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalityBranding;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalitySeal;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

// Public, read-only registry for the CARCANMADCARLAN municipality selector (pre-login).
// Anonymous BY DESIGN — the public landing needs the list/status before any authentication,
// mirroring the existing public SetupController. Returns only non-sensitive presentation
// fields (no operational data).
[AllowAnonymous]
public class MunicipalitiesController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MunicipalityDto>>> GetMunicipalities()
    {
        var result = await Sender.Send(new GetMunicipalitiesQuery());
        return HandleResponse(result);
    }

    /// <summary>
    /// Public pre-login branding for a single LGU, resolved by subdomain identifier (its TenantCode or Code).
    /// Lets a subdomain's login page theme itself (office name, seal) before any authentication.
    /// </summary>
    [HttpGet("{identifier}/branding")]
    public async Task<ActionResult<MunicipalityBrandingDto>> GetBranding(string identifier)
    {
        var result = await Sender.Send(new GetMunicipalityBrandingQuery(identifier));
        return HandleResponse(WithSealUrl(result, identifier));
    }

    /// <summary>
    /// Authenticated branding for the CALLER's own LGU (post-login), resolved from the JWT municipality
    /// claim — powers the in-app shell (office label/acronym, seal) data-driven per tenant. The literal
    /// "current" segment takes precedence over the "{identifier}" route above.
    /// </summary>
    [Authorize]
    [HttpGet("current/branding")]
    public async Task<ActionResult<MunicipalityBrandingDto>> GetCurrentBranding()
    {
        var result = await Sender.Send(new GetCurrentMunicipalityBrandingQuery());
        return HandleResponse(WithSealUrl(result, result.Value?.TenantCode));
    }

    /// <summary>
    /// An LGU's official seal, as an ordinary cacheable image.
    ///
    /// <para>
    /// Anonymous like the branding above, and for the same reason: the sign-in page shows the office's seal before
    /// anybody has authenticated. A seal is public identification, and it was already being served to anonymous
    /// callers inside the branding payload.
    /// </para>
    ///
    /// <para>
    /// The address carries a version taken from the seal's own bytes, so it may be cached hard: a re-uploaded seal is a
    /// different address and is fetched again, while an unchanged one is never fetched twice.
    /// </para>
    /// </summary>
    [HttpGet("{identifier}/seal")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetSeal(string identifier)
    {
        var result = await Sender.Send(new GetMunicipalitySealQuery(identifier));
        if (!result.IsSuccess || result.Value is null) return NotFound();

        var seal = result.Value;

        // The tag is handed to File rather than written as a header, which is what makes a conditional request work:
        // setting it by hand emits the tag but leaves the framework unaware of it, so a browser sending If-None-Match
        // was answered 200 with the whole image again. Measured against the deployed endpoint, which is how it was
        // caught. It matters little while the address is cached for a year, but a revalidating proxy or an evicted
        // cache should be answered 304 rather than resent 9 KB.
        return File(seal.Content, seal.ContentType, fileDownloadName: null,
                    lastModified: null,
                    entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(seal.ETag));
    }

    /// <summary>
    /// Replaces an embedded seal with the address of the seal endpoint, leaving every other field alone.
    ///
    /// <para>
    /// Done here rather than in the query because only the request knows the host the caller reached us on, and the
    /// portal, the collector app and the printed sheets all read the same field. They receive a short URL now instead
    /// of a data URI of some tens of kilobytes, so the value can be carried across a prerender boundary and cached by
    /// the browser. A seal already recorded as a file path is left exactly as it is: the web host serves it.
    /// </para>
    /// </summary>
    private Result<MunicipalityBrandingDto> WithSealUrl(Result<MunicipalityBrandingDto> result, string? identifier)
    {
        if (!result.IsSuccess || result.Value is not { } branding) return result;
        if (branding.SealPath is not { Length: > 0 } stored) return result;

        var url = SealUrl.From(Request, identifier, stored);
        return ReferenceEquals(url, stored) || url == stored
            ? result
            : Result<MunicipalityBrandingDto>.Success(branding with { SealPath = url });
    }

    /// <summary>
    /// The scheme the CALLER used, not the one that reached this process.
    ///
    /// <para>
    /// TLS terminates at the platform's proxy, so inside the container a request arrives over plain HTTP and
    /// <c>Request.Scheme</c> says "http". An http address for the seal, on a page served over https, is mixed content:
    /// the browser refuses to load it and the seal silently disappears. Caught before it shipped by reading the address
    /// the deployed API actually returned.
    /// </para>
    ///
    /// <para>
    /// Read from the forwarding header here rather than by turning on forwarded-headers processing for the whole
    /// application: that also rewrites the scheme every other component sees and the remote address the logs record,
    /// which is a wider change than one URL needs and would sit underneath the HTTPS redirect and HSTS.
    /// </para>
    /// </summary>
    /// <summary>
    /// The scheme the CALLER used. Kept as a thin call to the shared rule so the seal endpoint below and the Head's
    /// activation page cannot drift apart on the one detail that fails silently.
    /// </summary>
    private string PublicScheme() => SealUrl.PublicScheme(Request);
}
