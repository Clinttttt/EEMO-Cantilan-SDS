using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetTpmCollectionShadowReconciliation;

public sealed record GetTpmCollectionShadowReconciliationQuery(DateOnly From, DateOnly To)
    : IRequest<Result<TpmCollectionShadowReconciliationDto>>;
