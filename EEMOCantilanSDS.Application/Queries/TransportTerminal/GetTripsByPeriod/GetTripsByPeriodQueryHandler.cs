using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTripsByPeriod;

public class GetTripsByPeriodQueryHandler(ITrmRepository trmRepository)
    : IRequestHandler<GetTripsByPeriodQuery, Result<IReadOnlyList<TrmTripDto>>>
{
    public async Task<Result<IReadOnlyList<TrmTripDto>>> Handle(GetTripsByPeriodQuery request, CancellationToken ct)
    {
        var trips = await trmRepository.GetTripsByMonthAsync(request.Year, request.Month, ct);
        return Result<IReadOnlyList<TrmTripDto>>.Success(trips);
    }
}
