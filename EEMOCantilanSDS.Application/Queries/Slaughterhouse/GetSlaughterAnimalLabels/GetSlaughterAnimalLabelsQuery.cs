using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterAnimalLabels;

public sealed record GetSlaughterAnimalLabelsQuery : IRequest<Result<SlaughterAnimalLabelsDto>>;

public sealed class GetSlaughterAnimalLabelsQueryHandler(ISlaughterAnimalLabelProvider provider)
    : IRequestHandler<GetSlaughterAnimalLabelsQuery, Result<SlaughterAnimalLabelsDto>>
{
    public async Task<Result<SlaughterAnimalLabelsDto>> Handle(GetSlaughterAnimalLabelsQuery request, CancellationToken ct)
    {
        var labels = await provider.GetAsync(ct);
        return Result<SlaughterAnimalLabelsDto>.Success(new(labels.Hog, labels.Carabao, labels.Cow));
    }
}
