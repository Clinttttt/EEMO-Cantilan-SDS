using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmOverview;

public class GetTpmOverviewQueryHandler(
    ITpmRepository tpmRepo) : IRequestHandler<GetTpmOverviewQuery, Result<TpmOverviewDto>>
{
    public async Task<Result<TpmOverviewDto>> Handle(GetTpmOverviewQuery request, CancellationToken ct)
    {
        var overview = await tpmRepo.GetOverviewAsync(request.Year, request.Month, ct);
        return Result<TpmOverviewDto>.Success(overview);
    }
}
