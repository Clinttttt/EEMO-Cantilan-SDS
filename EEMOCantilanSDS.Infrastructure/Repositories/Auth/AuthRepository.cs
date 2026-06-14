using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class AuthRepository(AppDbContext context) : IAuthRepository
{
    public async Task<AdminUser?> GetAdminByUsernameAsync(string username, CancellationToken ct)
    {
        return await context.AdminUsers
            .FirstOrDefaultAsync(a => a.Username == username, ct);
    }
}
