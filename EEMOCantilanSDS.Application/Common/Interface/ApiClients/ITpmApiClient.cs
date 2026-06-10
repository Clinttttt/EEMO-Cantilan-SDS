using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Requests.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ITpmApiClient
{
    Task<Result<TpmOverviewDto>> GetOverviewAsync(int year, int month);
    Task<Result<IReadOnlyList<TpmMarketDayDto>>> GetMarketDaysAsync(int year, int month);
    Task<Result<IReadOnlyList<TpmVendorAttendanceDto>>> GetMonthAttendanceAsync(int year, int month);
    Task<Result<TpmHistoryDto>> GetHistoryAsync(int year);
    Task<Result<IReadOnlyList<TpmVendorAttendanceDto>>> GetVendorAttendanceAsync(DateOnly marketDate);
    Task<Result<TpmVendorAttendanceDto>> AddVendorAsync(AddVendorToMarketDayCommand command);
    Task<Result<bool>> MarkVendorPaidAsync(Guid attendanceId, MarkVendorPaidRequest request);
    Task<Result<bool>> SaveOrNumberAsync(Guid attendanceId, SaveOrNumberRequest request);
    Task<Result<bool>> UpdateVendorAsync(Guid attendanceId, UpdateTpmVendorRequest request);
}
