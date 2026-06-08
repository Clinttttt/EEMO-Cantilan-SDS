using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Transactions.GetRecentTransactions;

public class GetRecentTransactionsQueryHandler(ITransactionFeedRepository feedRepo)
    : IRequestHandler<GetRecentTransactionsQuery, Result<IReadOnlyList<TransactionFeedDto>>>
{
    public async Task<Result<IReadOnlyList<TransactionFeedDto>>> Handle(GetRecentTransactionsQuery request, CancellationToken ct)
    {
        var feed = await feedRepo.GetRecentTransactionsAsync(request.Facility, request.OnDate, request.Limit, ct);
        return Result<IReadOnlyList<TransactionFeedDto>>.Success(feed);
    }
}
