using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ITpmRepository
{
    // Vendors
    Task<TpmVendor?> GetVendorByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TpmVendor>> GetAllVendorsAsync(CancellationToken ct = default);
    Task AddVendorAsync(TpmVendor vendor, CancellationToken ct = default);
    
    // Attendance
    Task<TpmAttendance?> GetAttendanceByIdAsync(Guid id, CancellationToken ct = default);
    Task<TpmAttendance?> GetAttendanceAsync(Guid vendorId, DateOnly marketDate, CancellationToken ct = default);
    Task<IReadOnlyList<TpmAttendance>> GetAttendancesByDateAsync(DateOnly marketDate, CancellationToken ct = default);
    Task<IReadOnlyList<TpmAttendance>> GetAttendancesByMonthAsync(int year, int month, CancellationToken ct = default);
    Task AddAttendanceAsync(TpmAttendance attendance, CancellationToken ct = default);
    
    // Dashboard queries
    Task<TpmOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<TpmMarketDayDto>> GetMarketDaysAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<TpmVendorAttendanceDto>> GetVendorAttendanceAsync(DateOnly marketDate, CancellationToken ct = default);
    
    // Validation
    Task<bool> IsVendorNameUniqueAsync(string vendorName, CancellationToken ct = default);
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default);
}
