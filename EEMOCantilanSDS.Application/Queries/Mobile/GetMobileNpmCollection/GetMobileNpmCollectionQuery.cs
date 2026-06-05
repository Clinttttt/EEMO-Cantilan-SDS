using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;

public sealed record GetMobileNpmCollectionQuery(int Year, int Month)
    : IRequest<Result<MobileNpmCollectionDto>>;
