using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IUtilitiesApiClient
{
    Task<Result<UtilityRegisterDto>> GetRegisterAsync(int year, int month, MarketSection? section = null);
    Task<Result<UtilityBillEntryDto>> GetBillForEntryAsync(Guid stallId, int year, int month);
    Task<Result<IReadOnlyList<UtilityHistoryRowDto>>> GetStallHistoryAsync(Guid stallId);
    Task<Result<UtilityBillDto>> RecordReadingAsync(RecordUtilityReadingCommand command);
    Task<Result<UtilityBillDto>> RecordPaymentAsync(RecordUtilityPaymentCommand command);
}
