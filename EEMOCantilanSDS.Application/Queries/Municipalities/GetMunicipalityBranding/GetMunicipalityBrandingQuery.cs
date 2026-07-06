using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalityBranding
{
    /// <summary>
    /// Resolves a single LGU's public branding by its subdomain <paramref name="Identifier"/> (TenantCode or
    /// Code) for pre-login theming. Returns NotFound when the identifier matches no municipality.
    /// </summary>
    public record GetMunicipalityBrandingQuery(string Identifier) : IRequest<Result<MunicipalityBrandingDto>>;
}
