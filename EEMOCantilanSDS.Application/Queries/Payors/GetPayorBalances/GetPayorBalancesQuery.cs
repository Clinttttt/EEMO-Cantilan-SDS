using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorBalances;

/// <summary>
/// Outstanding balances for every stall linked to the authenticated payor. The payor is resolved
/// from the auth token server-side — never from client input.
/// </summary>
public record GetPayorBalancesQuery : IRequest<Result<IReadOnlyList<PayorStallBalanceDto>>>;
