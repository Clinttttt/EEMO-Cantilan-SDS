using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class ReportsApiClient(HttpClient http) : HandleResponse(http), IReportsApiClient
{
    public Task<Result<Application.Dtos.Monitoring.ContractsMonitoringPageDto>> GetContractsMonitoringAsync(Application.Dtos.Monitoring.ContractsMonitoringRequest request)
    {
        var parameters = new List<string>
        {
            "page=" + request.Page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "pageSize=" + request.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) parameters.Add(key + "=" + Uri.EscapeDataString(value.Trim())); }
        Add("search", request.Search); Add("operation", request.Operation); Add("status", request.Status);
        Add("occupancyBasis", request.OccupancyBasis); Add("sort", request.Sort);
        Add("facilityId", request.FacilityId?.ToString());
        Add("expiryWindow", request.ExpiryWindow?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("asOf", request.AsOf?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return GetAsync<Application.Dtos.Monitoring.ContractsMonitoringPageDto>("api/monitoring/contracts?" + string.Join("&", parameters));
    }
    public Task<Result<FollowUpQueueDto>> GetScopedFollowUpQueueAsync(int year, int month, FacilityCode? facility, string? operationCode) =>
        GetAsync<FollowUpQueueDto>($"api/Reports/follow-up?year={year}&month={month}" +
            (facility is { } code ? $"&facility={code}" : "") +
            (operationCode is not null ? $"&operationCode={Uri.EscapeDataString(operationCode)}" : ""));
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

    public async Task<Result<FollowUpQueueDto>> GetFollowUpHistoryAsync(int year, int month, bool wholeYear = false, bool allTime = false) =>
        await GetAsync<FollowUpQueueDto>($"api/Reports/follow-up/history?year={year}&month={month}&wholeYear={wholeYear.ToString().ToLowerInvariant()}&allTime={allTime.ToString().ToLowerInvariant()}");

    public async Task<Result<IReadOnlyList<int>>> GetFollowUpHistoryYearsAsync() =>
        await GetAsync<IReadOnlyList<int>>("api/Reports/follow-up/history/years");

    public async Task<Result<CollectionReportDto>> GetCollectionReportAsync(int year, int month) =>
        await GetAsync<CollectionReportDto>($"api/Reports/collection-report?year={year}&month={month}");
}
