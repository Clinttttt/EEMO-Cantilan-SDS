using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class AuthRepository(AppDbContext context) : IAuthRepository
{
    public async Task<AdminUser?> GetAdminByUsernameAsync(string username, CancellationToken ct)
    {
        // Login has no tenant context yet — the active LGU is DERIVED from the authenticated user (the
        // issued token then carries their MunicipalityId/TenantCode). So the lookup must span every LGU
        // (bypass the tenant query filter), while still excluding soft-deleted accounts. Subdomain-scoped
        // login is the Phase-5 refinement; until then usernames must stay unique across LGUs.
        return await context.AdminUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => !a.IsDeleted && a.Username == username, ct);
    }

    public async Task<AdminUser?> GetAdminByUsernameAsync(string username, Guid municipalityId, CancellationToken ct)
    {
        // Scoped login (?lgu={code}): the tenant is known up-front, so resolve the username WITHIN that
        // municipality. This prevents a username shared across LGUs from resolving to the wrong tenant's
        // account (which would otherwise fail the password check against the wrong hash and block a
        // legitimate admin). Bypass the query filter but pin MunicipalityId explicitly.
        return await context.AdminUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => !a.IsDeleted && a.Username == username && a.MunicipalityId == municipalityId, ct);
    }
}
