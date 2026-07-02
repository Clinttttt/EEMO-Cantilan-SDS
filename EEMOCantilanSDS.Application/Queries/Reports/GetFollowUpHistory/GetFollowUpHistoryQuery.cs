using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistory;

/// <summary>
/// Follow-up History — the same action list as the live queue, but a read snapshot "as of" a PAST
/// collection period (chosen on the History page). Reuses the shared composer; the contract-attention
/// and online-awaiting-OR sources are period-scoped so a past month reflects the state that would have
/// shown then, rather than today's.
/// </summary>
public record GetFollowUpHistoryQuery(int Year, int Month) : IRequest<Result<FollowUpQueueDto>>;
