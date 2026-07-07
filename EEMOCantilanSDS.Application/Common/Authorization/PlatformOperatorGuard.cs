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
