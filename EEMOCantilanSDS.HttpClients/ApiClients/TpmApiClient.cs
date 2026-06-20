using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Requests.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class TpmApiClient(HttpClient http) : HandleResponse(http), ITpmApiClient
{
    public async Task<Result<TpmOverviewDto>> GetOverviewAsync(int year, int month) =>
        await GetAsync<TpmOverviewDto>($"api/tpm/overview?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<TpmMarketDayDto>>> GetMarketDaysAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<TpmMarketDayDto>>($"api/tpm/market-days?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<TpmVendorAttendanceDto>>> GetMonthAttendanceAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<TpmVendorAttendanceDto>>($"api/tpm/month-attendance?year={year}&month={month}");

    public async Task<Result<TpmHistoryDto>> GetHistoryAsync(int year) =>
        await GetAsync<TpmHistoryDto>($"api/tpm/history?year={year}");

    public async Task<Result<IReadOnlyList<TpmVendorAttendanceDto>>> GetVendorAttendanceAsync(DateOnly marketDate) =>
        await GetAsync<IReadOnlyList<TpmVendorAttendanceDto>>($"api/tpm/attendance?marketDate={marketDate:yyyy-MM-dd}");

    public async Task<Result<TpmVendorAttendanceDto>> AddVendorAsync(AddVendorToMarketDayCommand command) =>
        await PostAsync<AddVendorToMarketDayCommand, TpmVendorAttendanceDto>("api/tpm/attendance", command);

    public async Task<Result<bool>> MarkVendorPaidAsync(Guid attendanceId, MarkVendorPaidRequest request) =>
        await UpdateAsync<MarkVendorPaidRequest, bool>($"api/tpm/attendance/{attendanceId}/payment", request);

    public async Task<Result<bool>> SaveOrNumberAsync(Guid attendanceId, SaveOrNumberRequest request) =>
        await UpdateAsync<SaveOrNumberRequest, bool>($"api/tpm/attendance/{attendanceId}/or-number", request);

    public async Task<Result<bool>> UpdateVendorAsync(Guid attendanceId, UpdateTpmVendorRequest request) =>
        await UpdateAsync<UpdateTpmVendorRequest, bool>($"api/tpm/attendance/{attendanceId}", request);
}
