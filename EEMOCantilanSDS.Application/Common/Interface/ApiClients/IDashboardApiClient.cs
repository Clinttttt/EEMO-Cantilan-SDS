using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IDashboardApiClient
{
    Task<Result<DashboardOverviewDto>> GetOverviewAsync(int year, int month);
}
