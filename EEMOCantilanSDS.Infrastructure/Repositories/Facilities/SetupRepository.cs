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
        // First-run setup provisions the DEFAULT (Cantilan) LGU's Head. In a multi-LGU deployment every
        // other LGU has its own SuperAdmin (created at activation), so scope this to the default LGU —
        // otherwise activating another LGU would wrongly report Cantilan's Head setup as already complete.
        var defaultId = await context.Municipalities.IgnoreQueryFilters()
            .Where(m => m.IsDefault)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (defaultId is null)
            return false; // no registry yet → setup is still required

        return await context.Users
            .IgnoreQueryFilters()
            .OfType<AdminUser>()
            .AnyAsync(a => a.Role == AdminRole.SuperAdmin && !a.IsDeleted && a.MunicipalityId == defaultId, ct);
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
