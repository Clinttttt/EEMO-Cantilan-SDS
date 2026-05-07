using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;

public class GetSlaughterOverviewQueryHandler(
    ISlaughterRepository slaughterRepository) : IRequestHandler<GetSlaughterOverviewQuery, Result<SlaughterOverviewDto>>
{
    public async Task<Result<SlaughterOverviewDto>> Handle(GetSlaughterOverviewQuery request, CancellationToken ct)
    {
        var overview = await slaughterRepository.GetOverviewAsync(request.Year, request.Month, ct);
        return Result<SlaughterOverviewDto>.Success(overview);
    }
}
