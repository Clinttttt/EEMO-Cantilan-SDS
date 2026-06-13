using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorPaymentHistory;

public class GetPayorPaymentHistoryQueryHandler(
    IPayorRepository payorRepository,
    IPaymentRepository paymentRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetPayorPaymentHistoryQuery, Result<IReadOnlyList<PaymentHistoryDto>>>
{
    public async Task<Result<IReadOnlyList<PaymentHistoryDto>>> Handle(GetPayorPaymentHistoryQuery request, CancellationToken cancellationToken)
    {
        var payorId = currentUser.UserId;
        if (payorId is null)
            return Result<IReadOnlyList<PaymentHistoryDto>>.Unauthorized();

        // Authorization: a payor may only view history for stalls linked to their own account.
        if (!await payorRepository.LinkExistsAsync(payorId.Value, request.StallId, cancellationToken))
            return Result<IReadOnlyList<PaymentHistoryDto>>.Forbidden();

        var history = await paymentRepository.GetPaymentHistoryAsync(request.StallId, cancellationToken);
        return Result<IReadOnlyList<PaymentHistoryDto>>.Success(history);
    }
}
