using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPaymentHistory;

public record GetPaymentHistoryQuery(Guid StallId) : IRequest<Result<IReadOnlyList<PaymentHistoryDto>>>;
