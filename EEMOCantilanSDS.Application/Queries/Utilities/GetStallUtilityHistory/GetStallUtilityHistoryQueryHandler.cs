using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetStallUtilityHistory;

public class GetStallUtilityHistoryQueryHandler(IUtilityBillRepository utilityRepository)
    : IRequestHandler<GetStallUtilityHistoryQuery, Result<IReadOnlyList<UtilityHistoryRowDto>>>
{
    public async Task<Result<IReadOnlyList<UtilityHistoryRowDto>>> Handle(GetStallUtilityHistoryQuery request, CancellationToken ct)
    {
        var bills = await utilityRepository.GetAllForStallAsync(request.StallId, ct);
        var rows = bills.Select(UtilityHistoryRowDto.From).ToList();
        return Result<IReadOnlyList<UtilityHistoryRowDto>>.Success(rows);
    }
}
