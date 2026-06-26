using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetMarketClosures;

public class GetNpmMarketClosuresQueryHandler(INpmMarketClosureRepository closureRepository)
    : IRequestHandler<GetNpmMarketClosuresQuery, Result<IReadOnlyList<int>>>
{
    public async Task<Result<IReadOnlyList<int>>> Handle(GetNpmMarketClosuresQuery request, CancellationToken ct)
    {
        var list = await closureRepository.GetByMonthAsync(request.Year, request.Month, ct);
        IReadOnlyList<int> days = list.Select(x => x.ClosureDate.Day).Distinct().OrderBy(d => d).ToList();
        return Result<IReadOnlyList<int>>.Success(days);
    }
}
