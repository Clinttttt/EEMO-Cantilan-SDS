using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Dashboard.GetDashboardOverview;

public class GetDashboardOverviewQueryHandler(IDashboardRepository dashboardRepository)
    : IRequestHandler<GetDashboardOverviewQuery, Result<DashboardOverviewDto>>
{
    public async Task<Result<DashboardOverviewDto>> Handle(GetDashboardOverviewQuery request, CancellationToken ct)
    {
        var overview = await dashboardRepository.GetOverviewAsync(request.Year, request.Month, ct);
        return Result<DashboardOverviewDto>.Success(overview);
    }
}
