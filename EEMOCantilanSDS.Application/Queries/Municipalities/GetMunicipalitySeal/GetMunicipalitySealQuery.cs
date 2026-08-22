using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalitySeal
{
    /// <summary>
    /// An LGU's official seal as bytes, resolved by subdomain identifier (its TenantCode or Code), for serving as an
    /// ordinary image.
    ///
    /// <para>
    /// A seal is stored as a data URI on the municipality's record, which meant every branding response carried the
    /// whole image and no caller could keep it: the portal deliberately leaves the seal out of the state it carries
    /// from a prerendered page into the interactive one, because a large one can exceed the circuit's message limit
    /// and drop the connection. The consequence was visible on every refresh, as the seal slot emptying and refilling.
    /// Served as an image with its own address, it is fetched once, cached by the browser, and small enough to carry.
    /// </para>
    /// </summary>
    public record GetMunicipalitySealQuery(string Identifier) : IRequest<Result<MunicipalitySealDto>>;

    /// <summary>An LGU's seal, decoded and ready to write to a response.</summary>
    public record MunicipalitySealDto(byte[] Content, string ContentType, string ETag);
}
