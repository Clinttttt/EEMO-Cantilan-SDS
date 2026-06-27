using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetClosedStallAccounts;

/// <summary>
/// Returns every inactive stall account (closed or expired) for the register. Filtering by
/// year/facility/search is done client-side — the set is small (only inactive accounts).
/// </summary>
public record GetClosedStallAccountsQuery() : IRequest<Result<IReadOnlyList<ClosedStallAccountDto>>>;
