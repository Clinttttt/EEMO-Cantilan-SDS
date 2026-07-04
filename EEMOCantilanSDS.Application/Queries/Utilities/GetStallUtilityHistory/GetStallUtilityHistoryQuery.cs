using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetStallUtilityHistory;

/// <summary>Full utility history for one stall (all months), newest first.</summary>
public record GetStallUtilityHistoryQuery(Guid StallId)
    : IRequest<Result<IReadOnlyList<UtilityHistoryRowDto>>>;
