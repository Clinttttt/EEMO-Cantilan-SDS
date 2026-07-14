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
        FacilityCode? facility = null,
        bool allTime = false)
    {
        var query = $"api/Reports/financial?period={period}&year={year}";

        if (month.HasValue)
            query += $"&month={month.Value}";

        if (facility.HasValue)
            query += $"&facility={facility.Value}";

        if (allTime)
            query += "&allTime=true";

        return await GetAsync<FinancialReportDto>(query);
    }

    public async Task<Result<FollowUpQueueDto>> GetFollowUpQueueAsync(int year, int month) =>
        await GetAsync<FollowUpQueueDto>($"api/Reports/follow-up?year={year}&month={month}");

    public async Task<Result<FollowUpQueueDto>> GetFollowUpHistoryAsync(int year, int month, bool wholeYear = false) =>
        await GetAsync<FollowUpQueueDto>($"api/Reports/follow-up/history?year={year}&month={month}&wholeYear={wholeYear.ToString().ToLowerInvariant()}");

    public async Task<Result<IReadOnlyList<int>>> GetFollowUpHistoryYearsAsync() =>
        await GetAsync<IReadOnlyList<int>>("api/Reports/follow-up/history/years");

    public async Task<Result<CollectionReportDto>> GetCollectionReportAsync(int year, int month) =>
        await GetAsync<CollectionReportDto>($"api/Reports/collection-report?year={year}&month={month}");
}
