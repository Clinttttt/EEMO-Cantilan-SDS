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
}
