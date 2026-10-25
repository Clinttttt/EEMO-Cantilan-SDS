using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterTransactions;

public class GetSlaughterTransactionsQueryHandler(
    ISlaughterRepository slaughterRepository) : IRequestHandler<GetSlaughterTransactionsQuery, Result<IReadOnlyList<SlaughterTransactionDto>>>
{
    public async Task<Result<IReadOnlyList<SlaughterTransactionDto>>> Handle(GetSlaughterTransactionsQuery request, CancellationToken ct)
    {
        // The office's list and report screens read the same set as the register: legacy and canonical (SRC) transactions.
        var transactions = await slaughterRepository.GetWorkspaceTransactionsByMonthAsync(request.Year, request.Month, ct);
        return Result<IReadOnlyList<SlaughterTransactionDto>>.Success(transactions);
    }
}
