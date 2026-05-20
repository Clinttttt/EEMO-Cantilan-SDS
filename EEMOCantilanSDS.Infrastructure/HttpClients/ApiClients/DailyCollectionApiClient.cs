using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class DailyCollectionApiClient(HttpClient http) : HandleResponse(http), IDailyCollectionApiClient
{
    public async Task<Result<bool>> RecordDailyCollectionAsync(RecordDailyCollectionCommand command) =>
        await PostAsync<RecordDailyCollectionCommand, bool>("api/DailyCollections/record", command);

    public async Task<Result<DailyCollectionMonthDto>> GetDailyCollectionMonthAsync(Guid stallId, int year, int month) =>
        await GetAsync<DailyCollectionMonthDto>($"api/DailyCollections/stall/{stallId}/month?year={year}&month={month}");
}
