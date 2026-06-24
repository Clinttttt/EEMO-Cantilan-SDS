using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;

/// <summary>
/// All-facility (or single-facility) financial report for the admin Reports page.
/// <paramref name="Facility"/> null = all facilities. <paramref name="Month"/> is required for the
/// Monthly period and ignored otherwise.
/// </summary>
public record GetFinancialReportQuery(
    ReportPeriod Period,
    int Year,
    int? Month,
    FacilityCode? Facility
) : IRequest<Result<FinancialReportDto>>;
