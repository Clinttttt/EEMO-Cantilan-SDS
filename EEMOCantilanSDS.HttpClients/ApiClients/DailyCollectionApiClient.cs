using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class DailyCollectionApiClient(HttpClient http) : HandleResponse(http), IDailyCollectionApiClient
{
    public async Task<Result<bool>> RecordDailyCollectionAsync(RecordDailyCollectionCommand command) =>
        await PostAsync<RecordDailyCollectionCommand, bool>("api/DailyCollections/record", command);

    public async Task<Result<DailyCollectionMonthDto>> GetDailyCollectionMonthAsync(Guid stallId, int year, int month) =>
        await GetAsync<DailyCollectionMonthDto>($"api/DailyCollections/stall/{stallId}/month?year={year}&month={month}");

    public async Task<Result<bool>> SaveOrNumberAsync(SaveDailyCollectionOrNumberCommand command) =>
        await PostAsync<SaveDailyCollectionOrNumberCommand, bool>("api/DailyCollections/or", command);

    public async Task<Result<bool>> SaveOrForDaysAsync(SaveDailyCollectionOrForDaysCommand command) =>
        await PostAsync<SaveDailyCollectionOrForDaysCommand, bool>("api/DailyCollections/or-days", command);

    public async Task<Result<bool>> SettleNpmMonthAsync(SettleNpmMonthCommand command) =>
        await PostAsync<SettleNpmMonthCommand, bool>("api/DailyCollections/settle-month", command);
}
