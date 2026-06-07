using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterHistory;

public class GetSlaughterHistoryQueryHandler(
    ISlaughterRepository slaughterRepository) : IRequestHandler<GetSlaughterHistoryQuery, Result<SlaughterHistoryDto>>
{
    public async Task<Result<SlaughterHistoryDto>> Handle(GetSlaughterHistoryQuery request, CancellationToken ct)
    {
        var history = await slaughterRepository.GetHistoryAsync(request.Year, ct);
        return Result<SlaughterHistoryDto>.Success(history);
    }
}
