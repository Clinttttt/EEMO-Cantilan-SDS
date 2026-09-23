using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassifications;

/// <summary>Lists the caller's revenue classifications with policy effective on the requested business date.</summary>
public sealed record GetRevenueClassificationsQuery(DateOnly? AsOf = null)
    : IRequest<Result<IReadOnlyList<RevenueClassificationDto>>>;
