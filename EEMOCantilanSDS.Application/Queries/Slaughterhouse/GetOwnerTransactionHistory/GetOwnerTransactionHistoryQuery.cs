using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetOwnerTransactionHistory;

public record GetOwnerTransactionHistoryQuery(
    string OwnerName,
    int Year,
    int Month
) : IRequest<Result<OwnerTransactionHistoryDto>>;
