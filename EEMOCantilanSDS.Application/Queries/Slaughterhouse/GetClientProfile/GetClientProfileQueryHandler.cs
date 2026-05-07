using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetClientProfile;

public class GetClientProfileQueryHandler(ISlaughterRepository slaughterRepository) 
    : IRequestHandler<GetClientProfileQuery, Result<ClientProfileDto>>
{
    public async Task<Result<ClientProfileDto>> Handle(GetClientProfileQuery request, CancellationToken ct)
    {
        var profile = await slaughterRepository.GetClientProfileAsync(request.OwnerName, ct);
        
        if (profile is null)
            return Result<ClientProfileDto>.NotFound();
        
        return Result<ClientProfileDto>.Success(profile);
    }
}
