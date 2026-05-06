using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetVendorAttendance;

public class GetVendorAttendanceQueryHandler(
    ITpmRepository tpmRepo) : IRequestHandler<GetVendorAttendanceQuery, Result<IReadOnlyList<TpmVendorAttendanceDto>>>
{
    public async Task<Result<IReadOnlyList<TpmVendorAttendanceDto>>> Handle(GetVendorAttendanceQuery request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetVendorAttendanceAsync(request.MarketDate, ct);
        return Result<IReadOnlyList<TpmVendorAttendanceDto>>.Success(attendance);
    }
}
