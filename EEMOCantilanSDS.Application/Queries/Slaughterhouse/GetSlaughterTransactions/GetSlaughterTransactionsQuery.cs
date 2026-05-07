using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterTransactions;

public record GetSlaughterTransactionsQuery(
    int Year,
    int Month
) : IRequest<Result<IReadOnlyList<SlaughterTransactionDto>>>;
