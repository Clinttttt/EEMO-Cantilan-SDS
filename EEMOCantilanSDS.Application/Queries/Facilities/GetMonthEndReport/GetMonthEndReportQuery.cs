using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetMonthEndReport;

public record GetMonthEndReportQuery(int Year, int Month) : IRequest<Result<MonthEndReportDto>>;
