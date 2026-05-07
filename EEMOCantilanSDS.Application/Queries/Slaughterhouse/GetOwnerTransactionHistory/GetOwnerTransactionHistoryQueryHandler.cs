using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetOwnerTransactionHistory;

public class GetOwnerTransactionHistoryQueryHandler(
    ISlaughterRepository slaughterRepository) : IRequestHandler<GetOwnerTransactionHistoryQuery, Result<OwnerTransactionHistoryDto>>
{
    public async Task<Result<OwnerTransactionHistoryDto>> Handle(GetOwnerTransactionHistoryQuery request, CancellationToken ct)
    {
        var history = await slaughterRepository.GetOwnerTransactionHistoryAsync(request.OwnerName, request.Year, request.Month, ct);
        return Result<OwnerTransactionHistoryDto>.Success(history);
    }
}
