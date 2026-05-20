using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IDailyCollectionApiClient
{
    Task<Result<bool>> RecordDailyCollectionAsync(RecordDailyCollectionCommand command);
    Task<Result<DailyCollectionMonthDto>> GetDailyCollectionMonthAsync(Guid stallId, int year, int month);
}
