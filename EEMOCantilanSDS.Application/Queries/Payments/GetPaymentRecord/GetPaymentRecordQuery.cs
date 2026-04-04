using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPaymentRecord;

public record GetPaymentRecordQuery(Guid StallId, int Year, int Month) : IRequest<Result<PaymentRecordDto>>;
