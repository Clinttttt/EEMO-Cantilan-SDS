using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetDashboard;

public class GetOnlinePaymentDashboardQueryHandler(
    IOnlinePaymentRepository onlinePaymentRepository)
    : IRequestHandler<GetOnlinePaymentDashboardQuery, Result<OnlinePaymentDashboardDto>>
{
    private const int RecentLimit = 25;

    public async Task<Result<OnlinePaymentDashboardDto>> Handle(GetOnlinePaymentDashboardQuery request, CancellationToken cancellationToken)
    {
        var today = PhilippineTime.Today;
        var dto = await onlinePaymentRepository.GetDashboardAsync(today.Year, today.Month, RecentLimit, cancellationToken);
        return Result<OnlinePaymentDashboardDto>.Success(dto);
    }
}
