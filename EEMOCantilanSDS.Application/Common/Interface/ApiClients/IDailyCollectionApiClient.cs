using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IDailyCollectionApiClient
{
    Task<Result<bool>> RecordDailyCollectionAsync(RecordDailyCollectionCommand command);
    Task<Result<DailyCollectionMonthDto>> GetDailyCollectionMonthAsync(Guid stallId, int year, int month);
    Task<Result<bool>> SaveOrNumberAsync(SaveDailyCollectionOrNumberCommand command);
    Task<Result<bool>> SaveOrForDaysAsync(SaveDailyCollectionOrForDaysCommand command);
    Task<Result<bool>> SettleNpmMonthAsync(SettleNpmMonthCommand command);
}
