using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMarketDays;

public class GetMarketDaysQueryHandler(
    ITpmRepository tpmRepo) : IRequestHandler<GetMarketDaysQuery, Result<IReadOnlyList<TpmMarketDayDto>>>
{
    public async Task<Result<IReadOnlyList<TpmMarketDayDto>>> Handle(GetMarketDaysQuery request, CancellationToken ct)
    {
        var marketDays = await tpmRepo.GetMarketDaysAsync(request.Year, request.Month, ct);
        return Result<IReadOnlyList<TpmMarketDayDto>>.Success(marketDays);
    }
}
