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
        /// <summary>
        /// True only for a DEDICATED platform/console operator account (the <c>IsPlatformOperator</c> flag).
        /// Distinct from <see cref="IsCurrentAsync"/>, which also accepts the backward-compatible fallback.
        /// Use this for powers that must not reach across municipalities in the hands of a municipal officer
        /// — listing or acting on other LGUs' accounts, for instance.
        /// </summary>
        public static async Task<bool> IsDedicatedOperatorAsync(IAppDbContext context, ICurrentUserService currentUser, CancellationToken ct)
        {
            if (currentUser.UserId is not Guid userId) return false;

            var isOperator = await context.AdminUsers
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => (bool?)u.IsPlatformOperator)
                .FirstOrDefaultAsync(ct);

            return isOperator == true;
        }

        public static async Task<bool> IsCurrentAsync(IAppDbContext context, ICurrentUserService currentUser, CancellationToken ct)
        {
            // Primary: a dedicated platform/console operator (the IsPlatformOperator flag), independent of any
            // municipality's Head role.
            var isDedicated = await IsDedicatedOperatorAsync(context, currentUser, ct);

            var defaultMunicipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(ct);

            var isDefaultTenant = defaultMunicipalityId is not null
                                  && currentUser.MunicipalityId == defaultMunicipalityId;

            // The decision itself lives in PlatformOperatorPolicy, so the API's authorization policy and this guard
            // cannot drift apart. They differ only in where the two facts come from: claims there, the database here.
            return PlatformOperatorPolicy.IsOperator(isDedicated, currentUser.Role, isDefaultTenant);
        }
    }
}
