using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IAuthRepository
{
    Task<AdminUser?> GetAdminByUsernameAsync(string username, CancellationToken ct);
}
