using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Authorization
{
    /// <summary>
    /// Determines whether the current caller is the <b>platform operator</b> — a SuperAdmin of the DEFAULT
    /// (Cantilan) municipality. Onboarding/activation are system-owner actions, so a per-LGU Head can never
    /// perform them. Mirrors the check inlined in <c>ActivateMunicipalityCommandHandler</c>.
    /// </summary>
    public static class PlatformOperatorGuard
    {
        public static async Task<bool> IsCurrentAsync(IAppDbContext context, ICurrentUserService currentUser, CancellationToken ct)
        {
            // Primary: a dedicated platform/console operator (the IsPlatformOperator flag), independent of any
            // municipality's Head role.
            if (currentUser.UserId is Guid userId)
            {
                var isOperator = await context.AdminUsers
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == userId)
                    .Select(u => (bool?)u.IsPlatformOperator)
                    .FirstOrDefaultAsync(ct);
                if (isOperator == true) return true;
            }

            // Backward-compatible fallback (temporary, until a dedicated console admin exists): a SuperAdmin
            // of the DEFAULT municipality (Cantilan's Head) may still operate onboarding.
            if (!string.Equals(currentUser.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                return false;

            var defaultMunicipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(ct);

            return defaultMunicipalityId is not null && currentUser.MunicipalityId == defaultMunicipalityId;
        }
    }
}
