using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;

public record GetFacilityReportsQuery(
    FacilityCode FacilityCode,
    ReportPeriod Period,
    int Year,
    int? Month,
    int? WeekNumber
) : IRequest<Result<FacilityReportsDto>>;
