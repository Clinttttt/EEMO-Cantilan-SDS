using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorPaymentHistory;

/// <summary>
/// 12-month payment history for one of the payor's stalls. The handler verifies the stall is linked
/// to the authenticated payor before returning any data.
/// </summary>
public record GetPayorPaymentHistoryQuery(Guid StallId) : IRequest<Result<IReadOnlyList<PaymentHistoryDto>>>;
