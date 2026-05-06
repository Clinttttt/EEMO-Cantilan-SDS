using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporterProfile;

public class GetTransporterProfileQueryHandler(
    ITrmRepository trmRepo) : IRequestHandler<GetTransporterProfileQuery, Result<TrmTransporterProfileDto>>
{
    public async Task<Result<TrmTransporterProfileDto>> Handle(GetTransporterProfileQuery request, CancellationToken ct)
    {
        var transporter = await trmRepo.GetTransporterByIdAsync(request.TransporterId, ct);
        if (transporter == null)
            return Result<TrmTransporterProfileDto>.NotFound();

        var profile = await trmRepo.GetTransporterProfileAsync(request.TransporterId, ct);
        return Result<TrmTransporterProfileDto>.Success(profile);
    }
}
