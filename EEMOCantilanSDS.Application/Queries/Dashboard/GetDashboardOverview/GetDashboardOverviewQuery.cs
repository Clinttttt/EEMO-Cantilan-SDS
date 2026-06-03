using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Dashboard.GetDashboardOverview;

public record GetDashboardOverviewQuery(int Year, int Month) : IRequest<Result<DashboardOverviewDto>>;
