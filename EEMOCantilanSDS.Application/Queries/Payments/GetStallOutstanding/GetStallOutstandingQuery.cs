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
/// <param name="Year">
/// With <paramref name="Month"/>, the billing period being collected on. A screen showing a PAST period must be
/// answered by the lessee who held the stall then — otherwise opening a 2025 row lists the current occupant's 2026
/// months, which belong to somebody else entirely. Ignored when a term is named outright.
/// </param>
/// <param name="Month">See <paramref name="Year"/>.</param>
public record GetStallOutstandingQuery(
    Guid StallId,
    Guid? ContractId = null,
    int? Year = null,
    int? Month = null) : IRequest<Result<IReadOnlyList<PaymentHistoryDto>>>;
