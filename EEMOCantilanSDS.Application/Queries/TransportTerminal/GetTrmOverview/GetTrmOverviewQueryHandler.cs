using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTrmOverview;

public class GetTrmOverviewQueryHandler(
    ITrmRepository trmRepo) : IRequestHandler<GetTrmOverviewQuery, Result<TrmOverviewDto>>
{
    public async Task<Result<TrmOverviewDto>> Handle(GetTrmOverviewQuery request, CancellationToken ct)
    {
        var overview = await trmRepo.GetOverviewAsync(ct);
        return Result<TrmOverviewDto>.Success(overview);
    }
}
