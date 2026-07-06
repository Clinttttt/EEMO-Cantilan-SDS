using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class AdminRepository(AppDbContext context) : IAdminRepository
{
    public async Task<List<AdminListDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.AdminUsers
            .AsNoTracking()
            // SuperAdmin (the Head) first, then most recently created.
            .OrderBy(a => a.Role)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AdminListDto(
                a.Id,
                a.FullName!,
                a.Username!,
                a.Email!,
                a.Role,
                a.IsActive,
                a.MustChangePassword,
                a.LastLoginAt,
                a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task AddAsync(AdminUser admin, CancellationToken cancellationToken = default)
    {
        await context.AdminUsers.AddAsync(admin, cancellationToken);
    }

    // Username/email uniqueness must consider soft-deleted rows too: the unique indexes on the
    // Users table are NOT filtered, so a soft-deleted user's name still occupies it. IgnoreQueryFilters
    // bypasses the global IsDeleted filter so we don't report a taken name as available.
    public async Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default)
    {
        // Bypass the soft-delete filter (a taken name must not read as available) but scope to the caller's
        // municipality when resolved — usernames are unique per LGU. Empty tenant (first-admin setup) → global.
        var mid = context.CurrentMunicipalityId;
        return !await context.Users.IgnoreQueryFilters().AnyAsync(u => (mid == Guid.Empty || u.MunicipalityId == mid) && u.Username == username, cancellationToken);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default)
    {
        var mid = context.CurrentMunicipalityId;
        return !await context.Users.IgnoreQueryFilters().AnyAsync(u => (mid == Guid.Empty || u.MunicipalityId == mid) && u.Email == email, cancellationToken);
    }

    public async Task<int> CountOtherActiveSuperAdminsAsync(Guid excludingId, CancellationToken cancellationToken = default)
    {
        return await context.AdminUsers
            .CountAsync(a => a.IsActive
                && a.Role == AdminRole.SuperAdmin
                && a.Id != excludingId, cancellationToken);
    }
}
