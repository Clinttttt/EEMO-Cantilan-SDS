using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetExceptions;

public class GetOnlinePaymentExceptionsQueryHandler(IOnlinePaymentRepository onlinePaymentRepository, IClock clock)
    : IRequestHandler<GetOnlinePaymentExceptionsQuery, Result<IReadOnlyList<OnlinePaymentExceptionDto>>>
{
    public async Task<Result<IReadOnlyList<OnlinePaymentExceptionDto>>> Handle(GetOnlinePaymentExceptionsQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<OnlinePaymentExceptionDto>>.Success(
            await onlinePaymentRepository.GetExceptionsAsync(clock.UtcNow, cancellationToken));
}
