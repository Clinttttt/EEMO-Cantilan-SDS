using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalitySeal
{
    public class GetMunicipalitySealQueryHandler(IMunicipalityRepository municipalityRepository)
        : IRequestHandler<GetMunicipalitySealQuery, Result<MunicipalitySealDto>>
    {
        public async Task<Result<MunicipalitySealDto>> Handle(GetMunicipalitySealQuery request, CancellationToken ct)
        {
            var m = await municipalityRepository.GetByIdentifierAsync(request.Identifier, ct);

            if (m is null || string.IsNullOrWhiteSpace(m.SealPath))
                return Result<MunicipalitySealDto>.NotFound();

            return SealDataUri.Decode(m.SealPath) is { } seal
                ? Result<MunicipalitySealDto>.Success(seal)
                : Result<MunicipalitySealDto>.NotFound();
        }
    }
}
