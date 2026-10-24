using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetCurrentMunicipalityBranding
{
    public class GetCurrentMunicipalityBrandingQueryHandler(
        IMunicipalityRepository municipalityRepository,
        ICurrentMunicipalityAccessor municipality)
        : IRequestHandler<GetCurrentMunicipalityBrandingQuery, Result<MunicipalityBrandingDto>>
    {
        public async Task<Result<MunicipalityBrandingDto>> Handle(GetCurrentMunicipalityBrandingQuery request, CancellationToken ct)
        {
            if (municipality.MunicipalityId == Guid.Empty)
                return Result<MunicipalityBrandingDto>.Forbidden();

            // Current branding follows authenticated ownership, never the legacy string/default tenant fallback.
            var m = await municipalityRepository.GetByIdAsync(municipality.MunicipalityId, ct);

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
                m.Address,
                m.ReportSignatories,
                m.IsDefault));
        }
    }
}
