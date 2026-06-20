using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class DashboardApiClient(HttpClient http) : HandleResponse(http), IDashboardApiClient
{
    public async Task<Result<DashboardOverviewDto>> GetOverviewAsync(int year, int month) =>
        await GetAsync<DashboardOverviewDto>($"api/Dashboard/overview?year={year}&month={month}");
}
