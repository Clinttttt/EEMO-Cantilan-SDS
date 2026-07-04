using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class UtilitiesApiClient(HttpClient http) : HandleResponse(http), IUtilitiesApiClient
{
    public async Task<Result<UtilityRegisterDto>> GetRegisterAsync(int year, int month, MarketSection? section = null)
    {
        var url = $"api/utilities/register?year={year}&month={month}";
        if (section.HasValue) url += $"&section={section.Value}";
        return await GetAsync<UtilityRegisterDto>(url);
    }

    public async Task<Result<UtilityBillEntryDto>> GetBillForEntryAsync(Guid stallId, int year, int month) =>
        await GetAsync<UtilityBillEntryDto>($"api/utilities/bill?stallId={stallId}&year={year}&month={month}");

    public async Task<Result<IReadOnlyList<UtilityHistoryRowDto>>> GetStallHistoryAsync(Guid stallId) =>
        await GetAsync<IReadOnlyList<UtilityHistoryRowDto>>($"api/utilities/history?stallId={stallId}");

    public async Task<Result<UtilityBillDto>> RecordReadingAsync(RecordUtilityReadingCommand command) =>
        await PostAsync<RecordUtilityReadingCommand, UtilityBillDto>("api/utilities/reading", command);

    public async Task<Result<UtilityBillDto>> RecordPaymentAsync(RecordUtilityPaymentCommand command) =>
        await PostAsync<RecordUtilityPaymentCommand, UtilityBillDto>("api/utilities/payment", command);
}
