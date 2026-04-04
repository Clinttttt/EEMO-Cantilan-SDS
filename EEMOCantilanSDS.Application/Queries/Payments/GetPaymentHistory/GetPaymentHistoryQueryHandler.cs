using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPaymentHistory;

public class GetPaymentHistoryQueryHandler(IPaymentRepository paymentRepository) : IRequestHandler<GetPaymentHistoryQuery, Result<IReadOnlyList<PaymentHistoryDto>>>
{
    public async Task<Result<IReadOnlyList<PaymentHistoryDto>>> Handle(GetPaymentHistoryQuery request, CancellationToken ct)
    {
        var history = await paymentRepository.GetPaymentHistoryAsync(request.StallId, ct);
        return Result<IReadOnlyList<PaymentHistoryDto>>.Success(history);
    }
}
