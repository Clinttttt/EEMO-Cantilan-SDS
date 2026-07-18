using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileBindInfo;

public class GetMobileBindInfoQueryHandler(IMunicipalityRepository municipalityRepository)
    : IRequestHandler<GetMobileBindInfoQuery, Result<MobileBindInfoDto>>
{
    public async Task<Result<MobileBindInfoDto>> Handle(GetMobileBindInfoQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Result<MobileBindInfoDto>.NotFound();

        var municipality = await municipalityRepository.GetByBindTokenAsync(request.Token.Trim(), ct);
        if (municipality is null || !municipality.IsActive)
            return Result<MobileBindInfoDto>.NotFound();

        return Result<MobileBindInfoDto>.Success(new MobileBindInfoDto(
            municipality.Code,
            municipality.TenantCode,
            municipality.Name,
            municipality.Province,
            municipality.OfficeName,
            municipality.OfficeAcronym,
            municipality.SealPath));
    }
}
