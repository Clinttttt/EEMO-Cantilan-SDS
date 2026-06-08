using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTrmHistory;

public class GetTrmHistoryQueryHandler(ITrmRepository trmRepository)
    : IRequestHandler<GetTrmHistoryQuery, Result<TrmHistoryDto>>
{
    public async Task<Result<TrmHistoryDto>> Handle(GetTrmHistoryQuery request, CancellationToken ct)
    {
        var history = await trmRepository.GetHistoryAsync(request.Year, ct);
        return Result<TrmHistoryDto>.Success(history);
    }
}
