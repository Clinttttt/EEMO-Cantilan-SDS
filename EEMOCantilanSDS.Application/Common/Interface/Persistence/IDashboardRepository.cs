using EEMOCantilanSDS.Application.Dtos.Dashboard;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IDashboardRepository
{
    Task<DashboardOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct);
}
