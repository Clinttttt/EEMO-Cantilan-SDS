using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Transactions.GetRecentTransactions;

public record GetRecentTransactionsQuery(FacilityCode? Facility, DateOnly? OnDate, int Limit)
    : IRequest<Result<IReadOnlyList<TransactionFeedDto>>>;
