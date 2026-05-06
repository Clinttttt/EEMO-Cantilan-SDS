using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetVendorAttendance;

public record GetVendorAttendanceQuery(DateOnly MarketDate) : IRequest<Result<IReadOnlyList<TpmVendorAttendanceDto>>>;
