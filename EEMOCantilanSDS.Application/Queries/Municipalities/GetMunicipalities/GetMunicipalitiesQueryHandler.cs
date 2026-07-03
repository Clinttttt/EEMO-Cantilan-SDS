using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalities;

public class GetMunicipalitiesQueryHandler(IMunicipalityRepository municipalityRepository)
    : IRequestHandler<GetMunicipalitiesQuery, Result<IReadOnlyList<MunicipalityDto>>>
{
    public async Task<Result<IReadOnlyList<MunicipalityDto>>> Handle(GetMunicipalitiesQuery request, CancellationToken ct)
    {
        var municipalities = await municipalityRepository.GetAllAsync(ct);

        var dtos = municipalities
            .Select(m => new MunicipalityDto(
                m.Code,
                m.Name,
                m.Province,
                m.OfficeName,
                m.Status.ToString(),
                m.IsActive,
                m.IsDefault))
            .ToList();

        return Result<IReadOnlyList<MunicipalityDto>>.Success(dtos);
    }
}
