using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileSlaughterCollection;

public sealed record GetMobileSlaughterCollectionQuery(int Year, int Month, int Day)
    : IRequest<Result<MobileSlaughterCollectionDto>>;
