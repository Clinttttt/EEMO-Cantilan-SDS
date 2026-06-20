using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class TransactionsApiClient(HttpClient http) : HandleResponse(http), ITransactionsApiClient
{
    public async Task<Result<IReadOnlyList<TransactionFeedDto>>> GetRecentAsync(FacilityCode? facility, DateOnly? onDate, int limit = 100)
    {
        var query = $"?limit={limit}";
        if (facility is not null) query += $"&facility={facility}";
        if (onDate is { } d) query += $"&onDate={d:yyyy-MM-dd}";
        return await GetAsync<IReadOnlyList<TransactionFeedDto>>($"api/transactions/recent{query}");
    }
}
