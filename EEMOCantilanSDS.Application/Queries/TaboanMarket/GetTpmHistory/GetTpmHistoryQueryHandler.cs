using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmHistory;

public class GetTpmHistoryQueryHandler(ITpmRepository tpmRepo)
    : IRequestHandler<GetTpmHistoryQuery, Result<TpmHistoryDto>>
{
    public async Task<Result<TpmHistoryDto>> Handle(GetTpmHistoryQuery request, CancellationToken ct)
    {
        var history = await tpmRepo.GetHistoryAsync(request.Year, ct);
        return Result<TpmHistoryDto>.Success(history);
    }
}
