using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallOutstanding;

/// <summary>A stall's unpaid months (with balance) — powers the Pay-bill form.</summary>
/// <param name="StallId">The stall.</param>
/// <param name="ContractId">
/// Whose arrears to state. A stall outlives its lessees, so on a stall that has been re-let the answer differs by
/// term: naming one reports THAT lessee's unpaid months and nobody else's. Omitted for the sitting lessee, which is
/// what every collection screen wants.
/// </param>
public record GetStallOutstandingQuery(Guid StallId, Guid? ContractId = null) : IRequest<Result<IReadOnlyList<PaymentHistoryDto>>>;
