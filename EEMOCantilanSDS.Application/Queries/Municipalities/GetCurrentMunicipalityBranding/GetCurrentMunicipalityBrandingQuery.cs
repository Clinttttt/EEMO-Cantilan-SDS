using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetCurrentMunicipalityBranding
{
    /// <summary>
    /// Resolves the CALLER's own LGU branding (post-login), from the authenticated tenant context
    /// (the JWT <c>municipality</c> claim). Powers the in-app shell — office label/acronym + seal —
    /// data-driven per tenant. Returns NotFound if the tenant can't be resolved (falls back to the
    /// UI's built-in default, so Cantilan is unaffected).
    /// </summary>
    public record GetCurrentMunicipalityBrandingQuery : IRequest<Result<MunicipalityBrandingDto>>;
}
