using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetNpmWeighingShadowReconciliation;

public sealed record GetNpmWeighingShadowReconciliationQuery(DateOnly From, DateOnly To)
    : IRequest<Result<NpmWeighingShadowReconciliationDto>>;
