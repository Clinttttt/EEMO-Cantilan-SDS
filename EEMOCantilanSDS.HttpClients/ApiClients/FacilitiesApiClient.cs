using EEMOCantilanSDS.Application.Command.Facilities.AddFacility;
using EEMOCantilanSDS.Application.Command.Facilities.AddNpmCustomSection;
using EEMOCantilanSDS.Application.Command.Facilities.RemoveNpmCustomSection;
using EEMOCantilanSDS.Application.Command.Facilities.SetFacilityStatus;
using EEMOCantilanSDS.Application.Command.Facilities.UpdateFacility;
using EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class FacilitiesApiClient(HttpClient http) : HandleResponse(http), IFacilitiesApiClient
{
    public async Task<Result<IReadOnlyList<FacilitySidebarSummaryDto>>> GetFacilitySummariesAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<FacilitySidebarSummaryDto>>($"api/Facilities/summaries?year={year}&month={month}");

    public async Task<Result<FacilityReportsDto>> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month = null,
        int? weekNumber = null)
    {
        var query = $"api/Facilities/{facilityCode}/reports?period={period}&year={year}";
        
        if (month.HasValue)
            query += $"&month={month.Value}";
        
        if (weekNumber.HasValue)
            query += $"&weekNumber={weekNumber.Value}";
        
        return await GetAsync<FacilityReportsDto>(query);
    }

    public async Task<Result<FacilityHistoryDto>> GetFacilityHistoryAsync(FacilityCode facilityCode, int year) =>
        await GetAsync<FacilityHistoryDto>($"api/Facilities/{facilityCode}/history?year={year}");

    public async Task<Result<MonthEndReportDto>> GetMonthEndReportAsync(int year, int month) =>
        await GetAsync<MonthEndReportDto>($"api/Facilities/month-end-report?year={year}&month={month}");

    public async Task<Result<FacilityConfigurationDto>> GetFacilityConfigurationAsync() =>
        await GetAsync<FacilityConfigurationDto>("api/Facilities/configuration");

    public async Task<Result<bool>> AddFacilityAsync(AddFacilityCommand command) =>
        await PostAsync<AddFacilityCommand, bool>("api/Facilities", command);

    public async Task<Result<bool>> UpdateFacilityAsync(UpdateFacilityCommand command) =>
        await PutAsync<UpdateFacilityCommand, bool>("api/Facilities", command);

    public async Task<Result<bool>> SetFacilityRateAsync(FacilityCode facilityCode, FeeRateKey key, decimal amount) =>
        await PutAsync<SetFacilityRateCommand, bool>("api/facility-rates", new SetFacilityRateCommand(facilityCode, key, amount));

    public async Task<Result<bool>> SetFacilityStatusAsync(string code, bool active) =>
        await PutAsync<SetFacilityStatusCommand, bool>("api/Facilities/status", new SetFacilityStatusCommand(code, active));

    public async Task<Result<IReadOnlyList<NpmCustomSectionDto>>> GetNpmCustomSectionsAsync() =>
        await GetAsync<IReadOnlyList<NpmCustomSectionDto>>("api/Facilities/npm/custom-sections");

    public async Task<Result<bool>> AddNpmCustomSectionAsync(string name) =>
        await PostAsync<AddNpmCustomSectionCommand, bool>("api/Facilities/npm/custom-sections", new AddNpmCustomSectionCommand(name));

    public async Task<Result<bool>> RemoveNpmCustomSectionAsync(string name) =>
        await PostAsync<RemoveNpmCustomSectionCommand, bool>("api/Facilities/npm/custom-sections/remove", new RemoveNpmCustomSectionCommand(name));
}
