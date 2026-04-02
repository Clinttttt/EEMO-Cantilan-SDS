using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Seeders
{
    public static class SuperAdminSeeder
    {
        public static async Task SeedAsync(IAppDbContext context)
        {
            var exists = await context.AdminUsers.AnyAsync(s => s.Role == AdminRole.SuperAdmin);
            if (exists) { return; }

            var superAdmin = AdminUser.Create(
            fullName: "System Administrator",
            username: "superadmin",
            email: "admin@cantilan.gov.ph",
            password: "Admin@1234",          
            role: AdminRole.SuperAdmin       
        );
            await context.AdminUsers.AddAsync(superAdmin);
            await context.SaveChangesAsync();
        }
    }
}
