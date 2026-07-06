using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalityBranding
{
    public class GetMunicipalityBrandingQueryHandler(IMunicipalityRepository municipalityRepository)
        : IRequestHandler<GetMunicipalityBrandingQuery, Result<MunicipalityBrandingDto>>
    {
        public async Task<Result<MunicipalityBrandingDto>> Handle(GetMunicipalityBrandingQuery request, CancellationToken ct)
        {
            var m = await municipalityRepository.GetByIdentifierAsync(request.Identifier, ct);

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
