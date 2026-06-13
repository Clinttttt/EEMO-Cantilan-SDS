using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetAwaitingOr;

public class GetOnlinePaymentsAwaitingOrQueryHandler(
    IOnlinePaymentRepository onlinePaymentRepository)
    : IRequestHandler<GetOnlinePaymentsAwaitingOrQuery, Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>>
{
    public async Task<Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>> Handle(GetOnlinePaymentsAwaitingOrQuery request, CancellationToken cancellationToken)
    {
        var items = await onlinePaymentRepository.GetAwaitingOrAsync(cancellationToken);
        return Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>.Success(items);
    }
}
