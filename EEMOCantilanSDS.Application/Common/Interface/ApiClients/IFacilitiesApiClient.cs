using EEMOCantilanSDS.Application.Command.Facilities.AddFacility;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IFacilitiesApiClient
{
    Task<Result<IReadOnlyList<FacilitySidebarSummaryDto>>> GetFacilitySummariesAsync(int year, int month);

    Task<Result<FacilityReportsDto>> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month = null,
        int? weekNumber = null);

    Task<Result<FacilityHistoryDto>> GetFacilityHistoryAsync(
        FacilityCode facilityCode,
        int year);

    Task<Result<MonthEndReportDto>> GetMonthEndReportAsync(int year, int month);

    Task<Result<FacilityConfigurationDto>> GetFacilityConfigurationAsync();

    Task<Result<bool>> AddFacilityAsync(AddFacilityCommand command);
}
