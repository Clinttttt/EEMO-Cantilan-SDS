using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class SetupRepository(AppDbContext context) : ISetupRepository
{
    public async Task<bool> IsSuperAdminExistsAsync(CancellationToken ct)
    {
        // First-run setup provisions the DEFAULT (Cantilan) LGU's HEAD. Scope to the default LGU AND exclude
        // platform/console operators (IsPlatformOperator) — the console operator is stamped to the default
        // municipality with the SuperAdmin role but is a DIFFERENT identity from the LGU's Head, so it must
        // not make Cantilan's Head setup look already-done. Other LGUs' Heads are excluded by the tenant scope.
        var defaultId = await context.Municipalities.IgnoreQueryFilters()
            .Where(m => m.IsDefault)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (defaultId is null)
            return false; // no registry yet → setup is still required

        return await context.Users
            .IgnoreQueryFilters()
            .OfType<AdminUser>()
            .AnyAsync(a => a.Role == AdminRole.SuperAdmin && !a.IsDeleted && !a.IsPlatformOperator && a.MunicipalityId == defaultId, ct);
    }

    public async Task<Guid> GetDefaultMunicipalityIdAsync(CancellationToken ct)
        => await context.Municipalities.IgnoreQueryFilters()
            .Where(m => m.IsDefault)
            .Select(m => m.Id)
            .FirstOrDefaultAsync(ct);

    public async Task AddFirstAdminAsync(AdminUser admin, CancellationToken ct)
    {
        await context.Users.AddAsync(admin, ct);
    }
}
