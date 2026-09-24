using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetTrmCollectionShadowReconciliation;

public sealed record GetTrmCollectionShadowReconciliationQuery(DateOnly From, DateOnly To)
    : IRequest<Result<TrmCollectionShadowReconciliationDto>>;
