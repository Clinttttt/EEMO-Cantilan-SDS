using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetCurrentMunicipalityBranding
{
    public class GetCurrentMunicipalityBrandingQueryHandler(
        IMunicipalityRepository municipalityRepository,
        ITenantContext tenantContext)
        : IRequestHandler<GetCurrentMunicipalityBrandingQuery, Result<MunicipalityBrandingDto>>
    {
        public async Task<Result<MunicipalityBrandingDto>> Handle(GetCurrentMunicipalityBrandingQuery request, CancellationToken ct)
        {
            // The tenant code comes from the authenticated JWT claim (or the default when absent). Reuse the
            // same identifier resolver as the public endpoint so behaviour is consistent.
            var m = await municipalityRepository.GetByIdentifierAsync(tenantContext.TenantCode, ct);

            if (m is null)
                return Result<MunicipalityBrandingDto>.NotFound();

            return Result<MunicipalityBrandingDto>.Success(new MunicipalityBrandingDto(
                m.Code,
                m.TenantCode,
                m.Name,
                m.Province,
                m.OfficeName,
                m.SealPath,
                m.Status.ToString(),
                m.IsActive,
                m.OfficeAcronym,
                m.Address));
        }
    }
}
