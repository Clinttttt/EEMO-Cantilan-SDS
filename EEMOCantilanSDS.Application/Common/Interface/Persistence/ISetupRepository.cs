using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ISetupRepository
{
    Task<bool> IsSuperAdminExistsAsync(CancellationToken ct);
    Task AddFirstAdminAsync(AdminUser admin, CancellationToken ct);
}
