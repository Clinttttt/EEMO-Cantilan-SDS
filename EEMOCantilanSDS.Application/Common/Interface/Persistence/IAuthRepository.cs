using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IAuthRepository
{
    Task<AdminUser?> GetAdminByUsernameAsync(string username, CancellationToken ct);

    /// <summary>
    /// Tenant-scoped admin lookup: resolves the username WITHIN a specific municipality. Used by the
    /// scoped login (?lgu={code}) so a username shared across LGUs resolves to the correct tenant's
    /// account (the global overload would return an arbitrary match).
    /// </summary>
    Task<AdminUser?> GetAdminByUsernameAsync(string username, Guid municipalityId, CancellationToken ct);
}
