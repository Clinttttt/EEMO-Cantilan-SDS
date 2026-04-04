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
        return await context.Users
            .OfType<AdminUser>()
            .AnyAsync(a => a.Role == AdminRole.SuperAdmin, ct);
    }

    public async Task AddFirstAdminAsync(AdminUser admin, CancellationToken ct)
    {
        await context.Users.AddAsync(admin, ct);
    }
}
