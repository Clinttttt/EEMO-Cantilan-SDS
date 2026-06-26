using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;

/// <summary>
/// Admin Follow-up Queue, computed "as of" the given collection period (the dashboard passes the current
/// month). Composes existing canonical sources — no new aggregation — into a single action list.
/// </summary>
public record GetFollowUpQueueQuery(int Year, int Month) : IRequest<Result<FollowUpQueueDto>>;
