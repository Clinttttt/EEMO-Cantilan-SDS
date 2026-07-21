using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetNpmCustomSections;

public class GetNpmCustomSectionsQueryHandler(IFacilityRepository facilityRepo)
    : IRequestHandler<GetNpmCustomSectionsQuery, Result<IReadOnlyList<NpmCustomSectionDto>>>
{
    public async Task<Result<IReadOnlyList<NpmCustomSectionDto>>> Handle(GetNpmCustomSectionsQuery request, CancellationToken ct)
    {
        var sections = await facilityRepo.GetNpmCustomSectionsAsync(ct);
        return Result<IReadOnlyList<NpmCustomSectionDto>>.Success(sections);
    }
}
