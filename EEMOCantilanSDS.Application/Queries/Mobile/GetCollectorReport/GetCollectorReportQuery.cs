using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;

public record GetCollectorReportQuery(FacilityCode? Facility, int Year, int Month)
    : IRequest<Result<MobileCollectorReportDto>>;
