using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetGroupedSlaughterTransactions;

public class GetGroupedSlaughterTransactionsQueryHandler(
    ISlaughterRepository slaughterRepository) : IRequestHandler<GetGroupedSlaughterTransactionsQuery, Result<IReadOnlyList<OwnerTransactionGroupDto>>>
{
    public async Task<Result<IReadOnlyList<OwnerTransactionGroupDto>>> Handle(GetGroupedSlaughterTransactionsQuery request, CancellationToken ct)
    {
        var groups = await slaughterRepository.GetGroupedTransactionsByMonthAsync(request.Year, request.Month, ct);
        return Result<IReadOnlyList<OwnerTransactionGroupDto>>.Success(groups);
    }
}
