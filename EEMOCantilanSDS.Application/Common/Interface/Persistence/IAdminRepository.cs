using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IAdminRepository
{
    Task<List<AdminListDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(AdminUser admin, CancellationToken cancellationToken = default);

    // Username/email must be unique across the whole Users table (admins and collectors share it).
    Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default);

    // Used to protect against demoting/deactivating the only remaining active SuperAdmin (the Head).
    Task<int> CountOtherActiveSuperAdminsAsync(Guid excludingId, CancellationToken cancellationToken = default);
}
