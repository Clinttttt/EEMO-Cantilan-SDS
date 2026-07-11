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

    Task<Result<FollowUpQueueDto>> GetFollowUpHistoryAsync(int year, int month);

    Task<Result<CollectionReportDto>> GetCollectionReportAsync(int year, int month);
}
