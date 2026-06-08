using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ITransactionsApiClient
{
    Task<Result<IReadOnlyList<TransactionFeedDto>>> GetRecentAsync(FacilityCode? facility, DateOnly? onDate, int limit = 100);
}
