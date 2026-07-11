using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallOutstanding;

/// <summary>A stall's unpaid months (with balance) across the whole contract — powers the Pay-bill form.</summary>
public record GetStallOutstandingQuery(Guid StallId) : IRequest<Result<IReadOnlyList<PaymentHistoryDto>>>;
