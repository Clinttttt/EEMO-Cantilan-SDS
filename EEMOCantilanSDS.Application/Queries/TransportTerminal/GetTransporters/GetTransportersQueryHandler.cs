using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporters;

public class GetTransportersQueryHandler(
    ITrmRepository trmRepo) : IRequestHandler<GetTransportersQuery, Result<IReadOnlyList<TrmTransporterListDto>>>
{
    public async Task<Result<IReadOnlyList<TrmTransporterListDto>>> Handle(GetTransportersQuery request, CancellationToken ct)
    {
        var transporters = await trmRepo.GetTransportersWithTodayTripsAsync(ct);
        return Result<IReadOnlyList<TrmTransporterListDto>>.Success(transporters);
    }
}
