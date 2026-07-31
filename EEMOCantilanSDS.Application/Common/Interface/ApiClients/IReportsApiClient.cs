using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IReportsApiClient
{
    Task<Result<FinancialReportDto>> GetFinancialReportAsync(
        ReportPeriod period,
        int year,
        int? month = null,
        FacilityCode? facility = null,
        bool allTime = false);

    Task<Result<FollowUpQueueDto>> GetFollowUpQueueAsync(int year, int month);

    /// <param name="allTime">
    /// True for the cumulative view: outstanding accounts with their whole balances, independent of any period.
    /// </param>
    Task<Result<FollowUpQueueDto>> GetFollowUpHistoryAsync(int year, int month, bool wholeYear = false, bool allTime = false);
    /// <summary>Years that have data, newest first — populates the Follow-up History year dropdown.</summary>
    Task<Result<IReadOnlyList<int>>> GetFollowUpHistoryYearsAsync();

    Task<Result<CollectionReportDto>> GetCollectionReportAsync(int year, int month);
}
