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
        FacilityCode? facility = null);
}
