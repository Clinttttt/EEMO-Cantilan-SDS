using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTodayTrips;

public class GetTodayTripsQueryHandler(
    ITrmRepository trmRepo) : IRequestHandler<GetTodayTripsQuery, Result<IReadOnlyList<TrmTripDto>>>
{
    public async Task<Result<IReadOnlyList<TrmTripDto>>> Handle(GetTodayTripsQuery request, CancellationToken ct)
    {
        var trips = await trmRepo.GetTodayTripsAsync(ct);
        return Result<IReadOnlyList<TrmTripDto>>.Success(trips);
    }
}
