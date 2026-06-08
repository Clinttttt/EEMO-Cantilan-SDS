using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMonthAttendance;

public class GetMonthAttendanceQueryHandler(ITpmRepository tpmRepo)
    : IRequestHandler<GetMonthAttendanceQuery, Result<IReadOnlyList<TpmVendorAttendanceDto>>>
{
    public async Task<Result<IReadOnlyList<TpmVendorAttendanceDto>>> Handle(GetMonthAttendanceQuery request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetMonthAttendanceAsync(request.Year, request.Month, ct);
        return Result<IReadOnlyList<TpmVendorAttendanceDto>>.Success(attendance);
    }
}
