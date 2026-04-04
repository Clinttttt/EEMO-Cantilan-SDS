using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPaymentRecord;

public class GetPaymentRecordQueryHandler(IPaymentRepository paymentRepository) : IRequestHandler<GetPaymentRecordQuery, Result<PaymentRecordDto>>
{
    public async Task<Result<PaymentRecordDto>> Handle(GetPaymentRecordQuery request, CancellationToken ct)
    {
        var payment = await paymentRepository.GetPaymentRecordAsync(request.StallId, request.Year, request.Month, ct);
        if (payment == null)
            return Result<PaymentRecordDto>.NotFound();

        return Result<PaymentRecordDto>.Success(payment);
    }
}
