using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetGroupedSlaughterTransactions;

public record GetGroupedSlaughterTransactionsQuery(
    int Year,
    int Month
) : IRequest<Result<IReadOnlyList<OwnerTransactionGroupDto>>>;
