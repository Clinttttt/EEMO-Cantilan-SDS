using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class ReportsApiClient(HttpClient http) : HandleResponse(http), IReportsApiClient
{
    public async Task<Result<FinancialReportDto>> GetFinancialReportAsync(
        ReportPeriod period,
        int year,
        int? month = null,
        FacilityCode? facility = null)
    {
        var query = $"api/Reports/financial?period={period}&year={year}";

        if (month.HasValue)
            query += $"&month={month.Value}";

        if (facility.HasValue)
            query += $"&facility={facility.Value}";

        return await GetAsync<FinancialReportDto>(query);
    }

    public async Task<Result<FollowUpQueueDto>> GetFollowUpQueueAsync(int year, int month) =>
        await GetAsync<FollowUpQueueDto>($"api/Reports/follow-up?year={year}&month={month}");

    public async Task<Result<CollectionReportDto>> GetCollectionReportAsync(int year, int month) =>
        await GetAsync<CollectionReportDto>($"api/Reports/collection-report?year={year}&month={month}");
}
