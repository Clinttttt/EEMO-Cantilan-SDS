using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassificationPolicyHistory;

/// <summary>Returns immutable policy versions for one classification owned by the caller's municipality.</summary>
public sealed record GetRevenueClassificationPolicyHistoryQuery(Guid ClassificationId)
    : IRequest<Result<IReadOnlyList<RevenueClassificationPolicyDto>>>;
