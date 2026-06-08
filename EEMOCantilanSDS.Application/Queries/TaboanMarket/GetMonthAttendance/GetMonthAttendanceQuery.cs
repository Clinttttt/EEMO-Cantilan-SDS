using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMonthAttendance;

public record GetMonthAttendanceQuery(int Year, int Month)
    : IRequest<Result<IReadOnlyList<TpmVendorAttendanceDto>>>;
